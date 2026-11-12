import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("migration", ROOT / "Tools/migrate_legacy.py")
migration = importlib.util.module_from_spec(spec)
spec.loader.exec_module(migration)


class MigrationTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        for name in ["Assets", "Packages", "ProjectSettings"]:
            (self.root / name).mkdir()
        self.guid = "3a436d05c58ca3e469fca091183cc2f1"
        self.other = "e4d8462229584b1ca873f7f3ede4c371"
        self.config = {"assets": [{"guid": self.guid, "legacyPath": "OSC/PiShockBolt_OSC.mat"}]}

    def asset(self, path, guid, extra=""):
        path = self.root / "Assets" / path
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(b"original asset bytes")
        Path(str(path) + ".meta").write_text("fileFormatVersion: 2\nguid: " + guid + "\n" + extra)
        return path

    def test_moved_asset_is_backed_up_and_reference_only_meta_is_preserved(self):
        moved = self.asset("Moved/BadgeMaterial.mat", self.guid)
        unrelated = self.asset("Furroxide/PiShockBolt/OSC/PiShockBolt_OSC.mat", self.other)
        reference = self.asset("ReferenceModel.fbx", "a" * 32, "externalObjects:\n  value: {fileID: 2100000, guid: " + self.guid + ", type: 2}\n")
        placement = self.asset("Furroxide/PiShockBolt/OSC/Presets/Local.prefab", "b" * 32)
        result = migration.migrate(self.root, self.config, apply=True)
        self.assertTrue(result["applied"])
        self.assertFalse(moved.exists())
        self.assertTrue(unrelated.exists() and reference.exists() and placement.exists())
        backup = Path(result["backup"])
        self.assertEqual(b"original asset bytes", (backup / "Assets/Moved/BadgeMaterial.mat").read_bytes())
        self.assertTrue((backup / "Assets/Moved/BadgeMaterial.mat.meta").exists())
        self.assertTrue((backup / "migration.json").exists())

    def test_dry_run_has_no_filesystem_changes(self):
        asset = self.asset("Badge.mat", self.guid)
        result = migration.migrate(self.root, self.config)
        self.assertEqual(1, len(result["matches"]))
        self.assertTrue(asset.exists())
        self.assertFalse((self.root / ".ShockOscMigrationBackup").exists())

    def test_duplicate_guid_stops_before_any_file_move(self):
        one = self.asset("One.mat", self.guid)
        two = self.asset("Two.mat", self.guid)
        with self.assertRaisesRegex(ValueError, "Duplicate legacy GUID"):
            migration.migrate(self.root, self.config, apply=True)
        self.assertTrue(one.exists() and two.exists())
        self.assertFalse((self.root / ".ShockOscMigrationBackup").exists())

    def test_open_unity_project_is_rejected(self):
        (self.root / "Temp").mkdir()
        (self.root / "Temp/UnityLockfile").touch()
        with self.assertRaisesRegex(ValueError, "Close Unity"):
            migration.migrate(self.root, self.config, apply=True)

    def test_failed_move_restores_already_moved_files(self):
        first = self.asset("Badge.mat", self.guid)
        second_guid = "c" * 32
        second = self.asset("Second.mat", second_guid)
        self.config["assets"].append({"guid": second_guid, "legacyPath": "Second.mat"})
        original_move = migration.shutil.move
        calls = []
        def fail_third(source, target):
            calls.append(source)
            if len(calls) == 3:
                raise OSError("simulated disk failure")
            return original_move(source, target)
        with patch.object(migration.shutil, "move", side_effect=fail_third):
            with self.assertRaisesRegex(OSError, "simulated disk failure"):
                migration.migrate(self.root, self.config, apply=True)
        self.assertTrue(first.exists() and second.exists())
        self.assertTrue(Path(str(first) + ".meta").exists() and Path(str(second) + ".meta").exists())
