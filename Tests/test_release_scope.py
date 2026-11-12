import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("release_package", ROOT / "Tools/package.py")
package = importlib.util.module_from_spec(spec)
spec.loader.exec_module(package)


class ReleaseScopeTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        config, _, manifest = package.load()
        manifest["version"] = "0.1.0"
        (self.root / "Tools").mkdir()
        (self.root / "Tools/package-assets.json").write_bytes(package.json_bytes(config))
        source = self.root / config["packagePath"]
        source.mkdir(parents=True)
        self.manifest_path = source / "package.json"
        self.manifest_path.write_bytes(package.json_bytes(manifest))
        fields = ["redistribution", "cleanInstall", "migration", "runtime", "liveOsc", "independentReview"]
        self.status = {"version": "0.1.0", **{
            field: {"status": "verified", "evidence": ["fixture evidence"]} for field in fields
        }}
        for field in ["redistribution", "liveOsc"]:
            self.status[field] = {"status": "pending", "evidence": [], "note": "Explicitly recorded limitation"}
        self.authorization = {
            "version": "0.1.0", "ownerAuthorized": True,
            "evidence": ["Owner authorized this public test version"],
            "acceptedPendingChecks": ["redistribution", "liveOsc"],
        }

    def check(self, status=None):
        (self.root / "release-status.json").write_bytes(package.json_bytes(status or self.status))
        package.check_publishable(self.root)

    def test_pending_checks_still_block_without_owner_authorization(self):
        with self.assertRaisesRegex(ValueError, "redistribution"):
            self.check()

    def test_authorized_test_release_accepts_only_its_recorded_limitations(self):
        self.status["testRelease"] = self.authorization
        self.check()
        self.assertEqual("pending", self.status["liveOsc"]["status"])
        self.assertEqual([], self.status["redistribution"]["evidence"])

    def test_authorization_is_bound_to_exact_version_and_evidence(self):
        for change in [{"version": "1.0.0"}, {"ownerAuthorized": False}, {"evidence": []}]:
            with self.subTest(change=change):
                status = copy.deepcopy(self.status)
                status["testRelease"] = dict(self.authorization, **change)
                with self.assertRaisesRegex(ValueError, "authorization for this version"):
                    self.check(status)

    def test_runtime_install_migration_and_review_cannot_be_waived(self):
        for field in ["runtime", "cleanInstall", "migration", "independentReview"]:
            with self.subTest(field=field):
                status = copy.deepcopy(self.status)
                status["testRelease"] = copy.deepcopy(self.authorization)
                status[field] = {"status": "pending", "evidence": [], "note": "Not tested"}
                with self.assertRaisesRegex(ValueError, field):
                    self.check(status)
                status["testRelease"]["acceptedPendingChecks"].append(field)
                with self.assertRaisesRegex(ValueError, "Only redistribution and live OSC"):
                    self.check(status)

    def test_pending_limitations_require_a_note(self):
        self.status["testRelease"] = self.authorization
        self.status["liveOsc"]["note"] = ""
        with self.assertRaisesRegex(ValueError, "liveOsc"):
            self.check()

    def test_test_authorization_does_not_carry_into_the_future_one_point_zero_release(self):
        manifest = json.loads(self.manifest_path.read_text())
        manifest["version"] = "1.0.0"
        self.manifest_path.write_bytes(package.json_bytes(manifest))
        self.status["version"] = "1.0.0"
        self.status["testRelease"] = self.authorization
        with self.assertRaisesRegex(ValueError, "authorization for this version"):
            self.check()


if __name__ == "__main__":
    unittest.main()
