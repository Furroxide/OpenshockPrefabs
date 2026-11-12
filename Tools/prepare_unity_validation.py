"""Prepare a disposable Unity validation project from locally installed dependencies."""
import argparse
import json
from pathlib import Path
import shutil
import zipfile

ROOT = Path(__file__).resolve().parents[1]
NAME = "com.furroxide.shockosc-avatar"


def prepare(source, output, mode, optimizer=False, vrcfury_archive=None):
    source, output = Path(source).resolve(), Path(output).resolve()
    package_version = json.loads((ROOT / "Unity/Packages" / NAME / "package.json").read_text())["version"]
    if output.exists():
        raise ValueError("Choose a new validation directory; existing projects are never overwritten")
    if output == source or source in output.parents and "Assets" in output.relative_to(source).parts:
        raise ValueError("Validation must be outside the production Assets directory")
    for directory in ["Assets/Editor", "Packages", "ProjectSettings", "RegistryPackages"]:
        (output / directory).mkdir(parents=True, exist_ok=True)
    source_lock = json.loads((source / "Packages/packages-lock.json").read_text())["dependencies"]
    source_manifest = json.loads((source / "Packages/manifest.json").read_text())["dependencies"]
    builtin = {name: value for name, value in source_manifest.items() if name.startswith("com.unity.modules.")}
    builtin["com.unity.ugui"] = "1.0.0"
    pending = ["com.vrchat.avatars", "com.vrchat.base", "com.vrcfury.vrcfury", "com.vrchat.core.vpm-resolver", "com.unity.test-framework"]
    if optimizer:
        pending.append("d4rkpl4y3r.d4rkavataroptimizer")
    copied = {}
    while pending:
        name = pending.pop()
        if name in copied or name in builtin:
            continue
        if source_lock.get(name, {}).get("source") == "builtin":
            builtin[name] = source_lock[name]["version"]
            continue
        if name == "com.vrcfury.vrcfury" and vrcfury_archive:
            with zipfile.ZipFile(vrcfury_archive) as archive:
                manifest = json.loads(archive.read("package.json"))
                if manifest["name"] != name:
                    raise ValueError("The VRCFury archive has an unexpected package name")
                archive.extractall(output / "Packages" / name)
            copied[name] = manifest["version"]
            pending.extend(manifest.get("dependencies", {}))
            pending.extend(manifest.get("vpmDependencies", {}))
            continue
        directory = source / "Packages" / name
        if not (directory / "package.json").exists():
            dependency_version = source_lock.get(name, {}).get("version")
            candidates = list((source / "Library/PackageCache").glob(name + "@*"))
            directory = next((p for p in candidates if p.name == name + "@" + str(dependency_version)), None)
            if directory is None:
                raise ValueError("Missing cached dependency: " + name)
        manifest = json.loads((directory / "package.json").read_text(encoding="utf-8-sig"))
        destination = output / ("RegistryPackages" if name.startswith("com.unity.") else "Packages") / name
        shutil.copytree(directory, destination, ignore=shutil.ignore_patterns(".git", "TrashBin", "Tests", "Tests.meta", "Tests~"))
        if name.startswith("com.unity."):
            builtin[name] = "file:../RegistryPackages/" + name
        copied[name] = manifest["version"]
        pending.extend(manifest.get("dependencies", {}))
        pending.extend(manifest.get("vpmDependencies", {}))
    (output / "Packages/manifest.json").write_text(json.dumps({"dependencies": builtin}, indent=2))
    vpm = {name: {"version": version} for name, version in copied.items() if not name.startswith("com.unity.")}
    (output / "Packages/vpm-manifest.json").write_text(json.dumps({"dependencies": vpm, "locked": vpm}, indent=2))
    shutil.copyfile(source / "ProjectSettings/ProjectVersion.txt", output / "ProjectSettings/ProjectVersion.txt")
    shutil.copyfile(ROOT / "Tools/UnityValidation.cs", output / "Assets/Editor/ShockOscPackageValidation.cs")
    original = output / "Assets/ValidationOriginal/PiShockBolt.fbx"
    original.parent.mkdir(parents=True)
    shutil.copyfile(source / "Assets/Furroxide/PiShockBolt/PiShockBolt.fbx", original)
    original_meta = (source / "Assets/Furroxide/PiShockBolt/PiShockBolt.fbx.meta").read_text()
    Path(str(original) + ".meta").write_text(original_meta.replace("197cdf1a99196754a86d5dd67f5cf5a5", "e4d8462229584b1ca873f7f3ede4c371"))
    # This source is outside Assets and Packages until the VPM installation step.
    with zipfile.ZipFile(ROOT / "dist" / f"{NAME}-{package_version}.zip") as archive:
        archive.extractall(output / "PackageSource")
    shutil.copyfile(ROOT / "dist" / f"PiShockBolt-Modular-OSC-{package_version}.unitypackage", output / "candidate.unitypackage")
    shutil.copyfile(ROOT / "dist" / f"PiShockBolt-Optimizer-{package_version}.unitypackage", output / "optimizer.unitypackage")
    if mode == "migration":
        old = source / "Assets/Furroxide/PiShockBolt"
        shutil.copytree(old, output / "Assets/Furroxide/PiShockBolt")
        # Preserve a moved asset's GUID while an unrelated file takes its old path.
        moved = output / "Assets/Furroxide/PiShockBolt/PiShockBolt_EmissionMask.png"
        target = output / "Assets/MovedBadgeMask.png"
        shutil.move(str(moved), str(target))
        shutil.move(str(moved) + ".meta", str(target) + ".meta")
        moved.write_bytes(target.read_bytes())
        Path(str(moved) + ".meta").write_text(Path(str(target) + ".meta").read_text().replace("dad1b9efe0a31c04bb231a3516404c85", "1234567890abcdef1234567890abcdef"))
    (output / "validation-config.json").write_text(json.dumps({"mode": mode, "optimizer": optimizer, "dependencyVersions": copied}, indent=2))
    return {"project": str(output), "mode": mode, "optimizer": optimizer, "dependencies": copied}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-project", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--mode", choices=["vpm", "unitypackage", "migration"], required=True)
    parser.add_argument("--optimizer", action="store_true")
    parser.add_argument("--vrcfury-archive", type=Path, help="Use an official VRCFury VCC ZIP instead of the installed package")
    args = parser.parse_args()
    print(json.dumps(prepare(args.source_project, args.output, args.mode, args.optimizer, args.vrcfury_archive), indent=2))
