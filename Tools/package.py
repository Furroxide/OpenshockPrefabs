"""Build reproducible VPM and Unity archives from the checked-in asset allowlist."""
from __future__ import annotations

import argparse
import gzip
import hashlib
import io
import json
from pathlib import Path, PurePosixPath
import re
import tarfile
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parents[1]
REPOSITORY = "Furroxide/OpenshockPrefabs"
LISTING_URL = "https://furroxide.github.io/OpenshockPrefabs/index.json"
GUID = re.compile(rb"^guid: ([a-f0-9]{32})$", re.M)
REFERENCES = re.compile(rb"guid: ([a-f0-9]{32})")
YAML = {".prefab", ".controller", ".anim", ".asset", ".mat", ".preset"}


def json_bytes(value):
    return (json.dumps(value, indent=2, ensure_ascii=False) + "\n").encode("utf-8")


def digest(data):
    return hashlib.sha256(data).hexdigest()


def safe_relative(value):
    path = PurePosixPath(value)
    if not value or "\\" in value or path.is_absolute() or ".." in path.parts or ":" in value:
        raise ValueError(f"Not a safe relative path: {value!r}")
    return path.as_posix()


def load(root=ROOT):
    config = json.loads((root / "Tools/package-assets.json").read_text(encoding="utf-8"))
    package = root / safe_relative(config["packagePath"])
    manifest = json.loads((package / "package.json").read_text(encoding="utf-8"))
    return config, package, manifest


def validate(root=ROOT):
    config, package, manifest = load(root)
    if manifest["name"] != config["packageName"]:
        raise ValueError("Manifest and asset-list package names differ")
    if not re.fullmatch(r"\d+\.\d+\.\d+", manifest["version"]):
        raise ValueError("Use an X.Y.Z package version")
    if manifest.get("legacyFolders") or manifest.get("legacyFiles"):
        raise ValueError("VPM legacy deletion is unsafe; use the exact-GUID backup helper")
    if manifest.get("vpmDependencies") != {
        "com.vrchat.avatars": ">=3.10.6 <3.11.0",
        "com.vrcfury.vrcfury": ">=1.1457.0 <2.0.0",
    }:
        raise ValueError("Unexpected required dependencies; keep the optimizer optional")

    expected, guids, paths = set(), set(), set()
    for asset in config["assets"]:
        rel = safe_relative(asset["path"])
        if rel.casefold() in paths:
            raise ValueError(f"Duplicate asset path: {rel}")
        paths.add(rel.casefold())
        if asset["guid"] in guids:
            raise ValueError(f"Duplicate GUID: {asset['guid']}")
        guids.add(asset["guid"])
        expected.update([rel, rel + ".meta"])
        data = (package / rel).read_bytes()
        if Path(rel).suffix == ".fbx" and re.search(rb"[A-Za-z]:[\\/]+Users[\\/]", data):
            raise ValueError("FBX contains a private or stale texture path: " + rel)
        match = GUID.search((package / (rel + ".meta")).read_bytes().replace(b"\r\n", b"\n"))
        if not match or match[1].decode() != asset["guid"]:
            raise ValueError(f"Asset GUID differs from the allowlist: {rel}")
        if "legacyPath" in asset:
            safe_relative(asset["legacyPath"])
        if Path(rel).suffix in YAML and config["legacyRoot"].encode() in data:
            raise ValueError(f"Unconverted asset reference path in {rel}")
        if bool(asset.get("optional")) != rel.startswith("Samples~/d4rkAvatarOptimizer/"):
            raise ValueError(f"Optional optimizer files must be outside Unity's base import: {rel}")

    for folder in config["folders"]:
        rel = safe_relative(folder["path"])
        if folder["guid"] in guids:
            raise ValueError(f"Duplicate folder GUID: {rel}")
        guids.add(folder["guid"])
        expected.add(rel + ".meta")
        match = GUID.search((package / (rel + ".meta")).read_bytes().replace(b"\r\n", b"\n"))
        if not match or match[1].decode() != folder["guid"]:
            raise ValueError(f"Folder GUID differs from allowlist: {rel}")

    actual = {p.relative_to(package).as_posix() for p in package.rglob("*") if p.is_file()}
    if actual != expected:
        raise ValueError(f"Package inventory differs: extra={sorted(actual - expected)}, missing={sorted(expected - actual)}")
    for asset in config["assets"]:
        path = package / asset["path"]
        if path.suffix not in YAML:
            continue
        allowed = guids | set(config["externalGuids"]) | {"0" * 32}
        if asset.get("optional"):
            allowed |= set(config["optionalExternalGuids"])
        unknown = {g.decode() for g in REFERENCES.findall(path.read_bytes())} - allowed
        if unknown:
            raise ValueError(f"Undeclared GUID dependencies in {asset['path']}: {sorted(unknown)}")
    expected_url = release_url(manifest, f"{manifest['name']}-{manifest['version']}.zip")
    if manifest["url"] != expected_url:
        raise ValueError(f"Manifest download URL must be {expected_url}")
    return config, package, manifest


def release_url(manifest, filename):
    return f"https://github.com/{REPOSITORY}/releases/download/v{manifest['version']}/{filename}"


def zip_package(package, members, output):
    with zipfile.ZipFile(output, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for name in sorted(members):
            info = zipfile.ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))
            info.create_system = 3
            info.external_attr = 0o100644 << 16
            info.compress_type = zipfile.ZIP_DEFLATED
            archive.writestr(info, (package / name).read_bytes(), compresslevel=9)


def unity_package(config, package, assets, output):
    # Unity packages are gzip-compressed tar archives keyed by each asset's existing GUID.
    with output.open("wb") as stream:
        with gzip.GzipFile(filename="", mode="wb", fileobj=stream, mtime=0, compresslevel=9) as gz:
            with tarfile.open(fileobj=gz, mode="w|", format=tarfile.USTAR_FORMAT) as archive:
                for asset in sorted(assets, key=lambda a: a["guid"]):
                    directory = tarfile.TarInfo(asset["guid"] + "/")
                    directory.type, directory.mode, directory.mtime = tarfile.DIRTYPE, 0o755, 0
                    archive.addfile(directory)
                    rel = asset["path"]
                    legacy = asset.get("legacyPath", rel.replace("Samples~/", "Extras/", 1))
                    path = config["legacyRoot"] + "/" + legacy
                    data = (package / rel).read_bytes()
                    if Path(rel).suffix in YAML:
                        data = data.replace(("Packages/" + config["packageName"] + "/").encode(),
                                            (config["legacyRoot"] + "/").encode())
                    for leaf, contents in [("asset", data), ("asset.meta", (package / (rel + ".meta")).read_bytes()),
                                           ("pathname", path.encode())]:
                        info = tarfile.TarInfo(asset["guid"] + "/" + leaf)
                        info.size, info.mtime, info.mode = len(contents), 0, 0o644
                        archive.addfile(info, io.BytesIO(contents))


def listing(packages):
    return {
        "name": "Furroxide ShockOSC Avatar Packages",
        "id": "com.furroxide.shockosc-avatar.listing",
        "url": LISTING_URL,
        "author": "221987073+furroxide@users.noreply.github.com",
        "packages": packages,
    }


def build(root=ROOT, output=None):
    config, package, manifest = validate(root)
    output = Path(output) if output else root / "dist"
    output.mkdir(parents=True, exist_ok=True)
    name, version = manifest["name"], manifest["version"]
    members = {a["path"] for a in config["assets"]}
    members |= {name + ".meta" for name in list(members)}
    members |= {f["path"] + ".meta" for f in config["folders"]}
    zip_path = output / f"{name}-{version}.zip"
    unity_path = output / f"PiShockBolt-Modular-OSC-{version}.unitypackage"
    extra_path = output / f"PiShockBolt-Optimizer-{version}.unitypackage"
    zip_package(package, members, zip_path)
    unity_package(config, package, [a for a in config["assets"] if not a.get("optional") and a["path"] != "package.json"], unity_path)
    unity_package(config, package, [a for a in config["assets"] if a.get("optional")], extra_path)
    (output / "package.json").write_bytes(json_bytes(manifest))
    artifacts = [zip_path, unity_path, extra_path, output / "package.json"]
    records = [{"name": p.name, "bytes": p.stat().st_size, "sha256": digest(p.read_bytes())} for p in artifacts]
    (output / "SHA256SUMS").write_text("".join(f"{r['sha256']}  {r['name']}\n" for r in records), encoding="utf-8", newline="\n")
    entry = dict(manifest, zipSHA256=digest(zip_path.read_bytes()))
    (output / "candidate-index.json").write_bytes(json_bytes(listing({name: {"versions": {version: entry}}})))
    (output / "release-manifest.json").write_bytes(json_bytes({"package": name, "version": version, "artifacts": records,
        "assets": [{"path": a["path"], "guid": a["guid"], "sha256": digest((package / a["path"]).read_bytes()),
                    "metaSha256": digest((package / (a["path"] + ".meta")).read_bytes()), "optional": a.get("optional", False)} for a in config["assets"]]}))
    return records


def check_publishable(root=ROOT):
    status = json.loads((root / "release-status.json").read_text(encoding="utf-8"))
    if status["version"] != load(root)[2]["version"]:
        raise ValueError("Release evidence belongs to a different package version")
    for field in ["redistribution", "cleanInstall", "migration", "runtime", "liveOsc", "independentReview"]:
        check = status[field]
        if check.get("status") != "verified" or not check.get("evidence"):
            raise ValueError(f"Public release pending: {field}. Record actual evidence in release-status.json.")


def fetch_json(url, token=None):
    headers = {"Accept": "application/vnd.github+json", "User-Agent": "ShockOSC-Avatar-Package-Listing"}
    if token:
        headers["Authorization"] = "Bearer " + token
    with urllib.request.urlopen(urllib.request.Request(url, headers=headers), timeout=30) as response:
        return json.load(response)


def build_listing(output, root=ROOT, token=None):
    # Build only from published releases; draft candidates must never appear in VCC.
    config, _, _ = load(root)
    versions, page = {}, 1
    while True:
        releases = fetch_json(f"https://api.github.com/repos/{REPOSITORY}/releases?per_page=100&page={page}", token)
        for release in releases:
            if release["draft"] or release["prerelease"] or not re.fullmatch(r"v\d+\.\d+\.\d+", release["tag_name"]):
                continue
            assets = {a["name"]: a for a in release["assets"]}
            if "package.json" not in assets:
                continue  # Inherited upstream releases are not VPM packages.
            manifest = fetch_json(assets["package.json"]["browser_download_url"])
            if manifest.get("name") != config["packageName"]:
                continue
            version = manifest["version"]
            name = f"{manifest['name']}-{version}.zip"
            if release["tag_name"] != "v" + version or name not in assets or "SHA256SUMS" not in assets:
                raise ValueError("Published VPM release is incomplete: " + release["tag_name"])
            with urllib.request.urlopen(assets[name]["browser_download_url"], timeout=30) as response:
                archive = response.read()
            with urllib.request.urlopen(assets["SHA256SUMS"]["browser_download_url"], timeout=30) as response:
                sums = dict(line.split("  ", 1)[::-1] for line in response.read().decode().splitlines())
            sha = digest(archive)
            if sums.get(name) != sha:
                raise ValueError(f"Published ZIP checksum mismatch for {version}")
            manifest.update(url=assets[name]["browser_download_url"], zipSHA256=sha)
            versions[version] = manifest
        if len(releases) < 100:
            break
        page += 1
    output = Path(output)
    output.mkdir(parents=True, exist_ok=True)
    (output / "index.json").write_bytes(json_bytes(listing({config["packageName"]: {"versions": versions}} if versions else {})))
    (output / "index.html").write_bytes((root / "WebsiteSource/index.html").read_bytes())
    return sorted(versions)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=["validate", "build", "check-publishable", "listing"])
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    if args.command == "validate":
        config, _, manifest = validate()
        print(f"Validated {len(config['assets'])} assets for {manifest['name']} {manifest['version']}")
    elif args.command == "build":
        print(json.dumps(build(output=args.output), indent=2))
    elif args.command == "check-publishable":
        check_publishable()
        print("Release evidence is recorded for all required checks.")
    else:
        import os
        print("Published versions:", build_listing(args.output or ROOT / "Website", token=os.environ.get("GITHUB_TOKEN")))


if __name__ == "__main__":
    main()
