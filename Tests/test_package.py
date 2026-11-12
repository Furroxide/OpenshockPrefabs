import copy
import importlib.util
import io
import json
from pathlib import Path
import shutil
import tarfile
import tempfile
import unittest
from unittest.mock import patch
import zipfile

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("package", ROOT / "Tools/package.py")
package = importlib.util.module_from_spec(spec)
spec.loader.exec_module(package)


class PackageTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.output = Path(self.temp.name)

    def build(self):
        return package.build(output=self.output)

    def test_reproducible_archives_and_checksum_listing(self):
        first = self.build()
        second = self.build()
        self.assertEqual(first, second)
        for record in first:
            self.assertEqual(record["sha256"], package.digest((self.output / record["name"]).read_bytes()))
        listing = json.loads((self.output / "candidate-index.json").read_text())
        entry = listing["packages"]["com.furroxide.shockosc-avatar"]["versions"][package.load()[2]["version"]]
        self.assertEqual(entry["zipSHA256"], first[0]["sha256"])
        self.assertNotIn("zipSHA256", json.loads((self.output / "package.json").read_text()))

    def test_vpm_zip_has_manifest_at_root_and_complete_meta_inventory(self):
        records = self.build()
        with zipfile.ZipFile(self.output / records[0]["name"]) as archive:
            names = set(archive.namelist())
            self.assertIn("package.json", names)
            self.assertIn("Samples~/d4rkAvatarOptimizer/PiShockBolt_Optimizer.preset", names)
            self.assertFalse(any("KhnFuCat" in p or "Assets/Openshock" in p for p in names))
            for name in names:
                if not name.endswith(".meta"):
                    self.assertIn(name + ".meta", names)
            self.assertEqual(len(names), len(archive.namelist()))

    def test_manual_archive_restores_paths_and_guids_without_optional_dependency(self):
        records = self.build()
        config, _, _ = package.load()
        expected = {a["guid"]: a for a in config["assets"] if not a.get("optional") and a["path"] != "package.json"}
        with tarfile.open(self.output / records[1]["name"]) as archive:
            actual = {}
            for member in archive.getmembers():
                if member.name.endswith("/pathname"):
                    guid = member.name.split("/")[0]
                    path = archive.extractfile(member).read().decode()
                    actual[guid] = path
                    asset = archive.extractfile(guid + "/asset").read()
                    if Path(path).suffix in package.YAML:
                        self.assertNotIn(b"Packages/com.furroxide.shockosc-avatar/", asset)
                        self.assertNotIn(b"15e0f77126a8696498bf16d06aa7991b", asset)
                    meta = archive.extractfile(guid + "/asset.meta").read()
                    self.assertEqual(guid, package.GUID.search(meta)[1].decode())
            self.assertEqual(set(actual), set(expected))
            for guid, path in actual.items():
                self.assertTrue(path.startswith("Assets/Furroxide/PiShockBolt/"))
                self.assertFalse(path.endswith(".preset"))

    def test_optional_archive_contains_only_sample_and_instructions(self):
        records = self.build()
        with tarfile.open(self.output / records[2]["name"]) as archive:
            paths = [archive.extractfile(m).read().decode() for m in archive.getmembers() if m.name.endswith("/pathname")]
            self.assertEqual(2, len(paths))
            self.assertIn("Assets/Furroxide/PiShockBolt/OSC/PiShockBolt_Optimizer.preset", paths)

    def test_migration_has_only_explicit_guid_matched_legacy_files(self):
        config, _, manifest = package.validate()
        self.assertNotIn("legacyFolders", manifest)
        self.assertNotIn("legacyFiles", manifest)
        targets = [a for a in config["assets"] if "legacyPath" in a]
        self.assertEqual(17, len(targets))
        for asset in targets:
            self.assertEqual(32, len(asset["guid"]))
            self.assertNotIn("Presets", asset["legacyPath"])

    def test_unknown_files_and_unexpected_references_fail_validation(self):
        fake = self.output / "repo"
        shutil.copytree(ROOT / "Unity", fake / "Unity")
        shutil.copytree(ROOT / "Tools", fake / "Tools")
        _, source, _ = package.load(fake)
        stray = source / "private-note.txt"
        stray.write_text("not intended for release")
        with self.assertRaisesRegex(ValueError, "inventory differs"):
            package.validate(fake)
        stray.unlink()
        prefab = source / "OSC/PiShockBolt_OSC.prefab"
        with prefab.open("ab") as stream:
            stream.write(b"\nreference: {guid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa}\n")
        with self.assertRaisesRegex(ValueError, "Undeclared GUID"):
            package.validate(fake)

    def test_current_texture_import_settings_and_disabled_receiver_survive(self):
        _, source, _ = package.validate()
        for name in ["PiShockBolt_OSC_BaseColor.png.meta", "PiShockBolt_EmissionMask.png.meta"]:
            meta = (source / name).read_text()
            self.assertIn("mipMapMode: 1", meta)
            self.assertIn("enableMipMap: 1", meta)
            self.assertIn("isReadable: 0", meta)
        prefab = (source / "OSC/PiShockBolt_OSC.prefab").read_text()
        receiver = prefab[prefab.index("guid: 80f1b8067b0760e4bb45023bc2e9de66"):]
        self.assertIn("allowSelf: 0", receiver)
        self.assertIn("allowOthers: 1", receiver)
        self.assertIn("localOnly: 1", receiver)
        self.assertIn("parameter: ShockOsc/Lower-Left", receiver)
        header = prefab[:prefab.index("guid: 80f1b8067b0760e4bb45023bc2e9de66")]
        self.assertEqual("0", header.rsplit("m_Enabled: ", 1)[1].splitlines()[0])

    def readiness_fixture(self):
        root = self.output / "readiness"
        root.mkdir()
        config, _, manifest = package.load()
        manifest["version"] = "1.0.0"
        (root / "Tools").mkdir()
        (root / "Tools/package-assets.json").write_bytes(package.json_bytes(config))
        source = root / config["packagePath"]
        source.mkdir(parents=True)
        (source / "package.json").write_bytes(package.json_bytes(manifest))
        fields = ["redistribution", "cleanInstall", "migration", "runtime", "liveOsc", "independentReview"]
        status = {"version": manifest["version"], **{field: {"status": "verified", "evidence": ["fixture evidence"]} for field in fields}}
        return root, status

    def test_publish_is_rejected_while_evidence_is_pending(self):
        root, status = self.readiness_fixture()
        status["redistribution"] = {"status": "unverified", "evidence": []}
        (root / "release-status.json").write_bytes(package.json_bytes(status))
        with self.assertRaisesRegex(ValueError, "redistribution"):
            package.check_publishable(root)

    def test_publish_accepts_complete_evidence_for_current_version(self):
        root, status = self.readiness_fixture()
        (root / "release-status.json").write_bytes(package.json_bytes(status))
        package.check_publishable(root)

    def test_publish_rejects_evidence_from_another_version(self):
        root, status = self.readiness_fixture()
        status["version"] = "0.9.0"
        (root / "release-status.json").write_bytes(package.json_bytes(status))
        with self.assertRaisesRegex(ValueError, "different package version"):
            package.check_publishable(root)

    def test_listing_ignores_drafts_prereleases_and_upstream_archives(self):
        releases = [
            {"draft": True, "prerelease": False, "tag_name": "v1.0.0", "assets": []},
            {"draft": False, "prerelease": True, "tag_name": "v1.0.0", "assets": []},
            {"draft": False, "prerelease": False, "tag_name": "Shockerv1.2", "assets": []},
        ]
        with patch.object(package, "fetch_json", return_value=releases):
            versions = package.build_listing(self.output)
        self.assertEqual([], versions)
        self.assertEqual({}, json.loads((self.output / "index.json").read_text())["packages"])

    def test_listing_verifies_download_before_advertising_release(self):
        config, _, manifest = package.load()
        manifest["version"] = "1.0.0"
        data = b"a downloaded VPM zip"
        assets = [{"name": n, "browser_download_url": "https://example.test/" + n} for n in ["package.json", "SHA256SUMS", "com.furroxide.shockosc-avatar-1.0.0.zip"]]
        releases = [{"draft": False, "prerelease": False, "tag_name": "v1.0.0", "assets": assets}]
        def fetch(url, token=None):
            return releases if "/releases?" in url else copy.deepcopy(manifest)
        def urlopen(url, **kwargs):
            if str(url).endswith("SHA256SUMS"):
                return io.BytesIO((package.digest(data) + "  com.furroxide.shockosc-avatar-1.0.0.zip\n").encode())
            return io.BytesIO(data)
        with patch.object(package, "fetch_json", side_effect=fetch), patch.object(package.urllib.request, "urlopen", side_effect=urlopen):
            self.assertEqual(["1.0.0"], package.build_listing(self.output))
        expected_hash = package.digest(data)
        def tampered_download(url, **kwargs):
            if str(url).endswith("SHA256SUMS"):
                return io.BytesIO((expected_hash + "  com.furroxide.shockosc-avatar-1.0.0.zip\n").encode())
            return io.BytesIO(b"tampered zip bytes")
        with patch.object(package, "fetch_json", side_effect=fetch), patch.object(package.urllib.request, "urlopen", side_effect=tampered_download):
            with self.assertRaisesRegex(ValueError, "checksum mismatch"):
                package.build_listing(self.output)

    def test_relative_paths_reject_parent_escape_and_windows_absolute_paths(self):
        for path in ["../private.txt", "/private.txt", "C:/private.txt", "folder\\private.txt"]:
            with self.assertRaises(ValueError):
                package.safe_relative(path)


if __name__ == "__main__":
    unittest.main()
