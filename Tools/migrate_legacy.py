"""Back up legacy badge assets by their own GUID before installing the VCC package.

Run with Unity closed. Without --apply this only reports the proposed file moves.
"""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import shutil

GUID = re.compile(rb"^guid:[ \t]*([a-f0-9]{32})[ \t]*$", re.M | re.I)


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def find_matches(project, config):
    project = Path(project).resolve(strict=True)
    if not all((project / name).is_dir() for name in ["Assets", "Packages", "ProjectSettings"]):
        raise ValueError("Not a Unity project: expected Assets, Packages and ProjectSettings")
    if (project / "Temp/UnityLockfile").exists():
        raise ValueError("Close Unity before preparing the VCC migration")
    assets = project / "Assets"
    if assets.is_symlink() or getattr(assets, "is_junction", lambda: False)() or not assets.resolve().is_relative_to(project):
        raise ValueError("Assets directory must be inside the selected project")
    assets = assets.resolve()
    # Match every asset exported by either manual archive, including newer scripts
    # and menus. The package manifest is the only allowlisted file not exported.
    targets = {a["guid"] for a in config["assets"] if a.get("path") != "package.json"}
    matches = {}
    for directory, folders, filenames in os.walk(assets, followlinks=False):
        for name in list(folders):
            child = Path(directory) / name
            if child.is_symlink() or getattr(child, "is_junction", lambda: False)():
                folders.remove(name)
        for name in filenames:
            if not name.endswith(".meta"):
                continue
            meta = Path(directory) / name
            asset = Path(str(meta)[:-5])
            if meta.is_symlink() or asset.is_symlink() or not asset.is_file():
                continue
            if not meta.resolve().is_relative_to(assets) or not asset.resolve().is_relative_to(assets):
                raise ValueError("Asset escapes the project Assets directory")
            own_guid = GUID.search(meta.read_bytes().replace(b"\r\n", b"\n"))
            if not own_guid or own_guid[1].decode().lower() not in targets:
                continue
            guid = own_guid[1].decode().lower()
            if guid in matches:
                raise ValueError("Duplicate legacy GUID; resolve both copies before migration: " + guid)
            matches[guid] = {"guid": guid, "path": asset.relative_to(project).as_posix(),
                             "sha256": sha(asset), "metaSha256": sha(meta)}
    return project, sorted(matches.values(), key=lambda item: item["path"])


def migrate(project, config, apply=False):
    project, matches = find_matches(project, config)
    result = {"applied": False, "matches": matches, "backup": None}
    if not apply or not matches:
        return result
    backup = project / ".ShockOscMigrationBackup" / datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")
    if not backup.resolve().is_relative_to(project):
        raise ValueError("Backup directory escapes the project")
    backup.mkdir(parents=True, exist_ok=False)
    result["backup"] = str(backup)
    moved = []
    try:
        # Validate the full proposal before moving the first file.
        for item in matches:
            source = project / item["path"]
            if sha(source) != item["sha256"] or sha(Path(str(source) + ".meta")) != item["metaSha256"]:
                raise ValueError("Asset changed after preflight: " + item["path"])
        for item in matches:
            for suffix in ["", ".meta"]:
                source = project / (item["path"] + suffix)
                target = backup / (item["path"] + suffix)
                if not source.resolve().is_relative_to(project / "Assets") or not target.resolve().is_relative_to(backup):
                    raise ValueError("Migration move escapes the intended directories")
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.move(str(source), str(target))
                moved.append((source, target))
        result["applied"] = True
        (backup / "migration.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    except Exception:
        for source, target in reversed(moved):
            if target.exists() and not source.exists():
                source.parent.mkdir(parents=True, exist_ok=True)
                shutil.move(str(target), str(source))
        raise
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", type=Path, required=True)
    parser.add_argument("--apply", action="store_true", help="Move the exact matched files into a backup outside Assets")
    args = parser.parse_args()
    config = json.loads(Path(__file__).with_name("package-assets.json").read_text(encoding="utf-8"))
    print(json.dumps(migrate(args.project, config, args.apply), indent=2))
