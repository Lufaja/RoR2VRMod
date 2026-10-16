import copy
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

REPO = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("restore_native", REPO / "tools/restore-native.py")
restore_native = importlib.util.module_from_spec(spec)
spec.loader.exec_module(restore_native)


def sha(value):
    return hashlib.sha256(value).hexdigest()


class RestoreTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="riskofvr-restore-")
        self.root = Path(self.temporary.name)
        self.state = self.root / ".vrmod-native"
        self.state.mkdir()
        self.ledger = {"version": 1, "disabled": False, "files": []}
        for index, name in enumerate(restore_native.ALLOWED):
            destination = self.root / name
            destination.parent.mkdir(parents=True, exist_ok=True)
            installed = f"installed-{index}".encode()
            destination.write_bytes(installed)
            original = b"original manifest" if index == 0 else None
            if original is not None:
                (self.state / f"{index}.original").write_bytes(original)
            self.ledger["files"].append({"path": name, "backup": f"{index}.original",
                "original_sha256": sha(original) if original is not None else None,
                "installed_sha256": sha(installed), "pending_sha256": None})
        self.save()

    def tearDown(self):
        self.temporary.cleanup()

    def save(self):
        (self.state / "ledger.json").write_text(json.dumps(self.ledger))

    def snapshot(self):
        return {str(p.relative_to(self.root)): p.read_bytes() for p in self.root.rglob("*") if p.is_file()}

    def test_dry_run_has_no_writes(self):
        before = self.snapshot()
        self.assertEqual(len(restore_native.restore(self.root)), 3)
        self.assertEqual(self.snapshot(), before)

    def test_restore_preserves_original_removes_added_and_is_idempotent(self):
        restore_native.restore(self.root, True)
        self.assertEqual((self.root / restore_native.ALLOWED[0]).read_bytes(), b"original manifest")
        self.assertFalse((self.root / restore_native.ALLOWED[1]).exists())
        self.assertFalse((self.root / restore_native.ALLOWED[2]).exists())
        self.assertTrue(json.loads((self.state / "ledger.json").read_text())["disabled"])
        before = self.snapshot()
        restore_native.restore(self.root, True)
        self.assertEqual(self.snapshot(), before)

    def test_external_edit_prevents_all_writes(self):
        (self.root / restore_native.ALLOWED[2]).write_bytes(b"later steam update")
        before = self.snapshot()
        with self.assertRaisesRegex(ValueError, "externally changed"):
            restore_native.restore(self.root, True)
        self.assertEqual(self.snapshot(), before)

    def test_missing_original_backup_prevents_all_writes(self):
        (self.state / "0.original").unlink()
        before = self.snapshot()
        with self.assertRaisesRegex(ValueError, "backup"):
            restore_native.restore(self.root, True)
        self.assertEqual(self.snapshot(), before)

    def test_changed_original_backup_prevents_all_writes(self):
        (self.state / "0.original").write_bytes(b"corrupted")
        before = self.snapshot()
        with self.assertRaisesRegex(ValueError, "backup"):
            restore_native.restore(self.root, True)
        self.assertEqual(self.snapshot(), before)

    def test_interrupted_install_pending_hash_is_recoverable(self):
        entry = self.ledger["files"][1]
        pending = b"new version committed before ledger update"
        entry["pending_sha256"] = sha(pending)
        (self.root / entry["path"]).write_bytes(pending)
        self.save()
        restore_native.restore(self.root, True)
        self.assertFalse((self.root / entry["path"]).exists())

    def test_interrupted_restore_is_recoverable(self):
        self.ledger["disabled"] = True
        (self.root / restore_native.ALLOWED[0]).write_bytes(b"original manifest")
        (self.root / restore_native.ALLOWED[1]).unlink()
        self.save()
        restore_native.restore(self.root, True)
        self.assertFalse((self.root / restore_native.ALLOWED[2]).exists())

    def test_missing_replaced_file_is_not_silently_recreated(self):
        (self.root / restore_native.ALLOWED[0]).unlink()
        with self.assertRaisesRegex(ValueError, "externally changed"):
            restore_native.restore(self.root, True)

    def test_path_traversal_duplicate_and_arbitrary_backup_refused(self):
        original = copy.deepcopy(self.ledger)
        for kind in ("traversal", "duplicate", "backup"):
            self.ledger = copy.deepcopy(original)
            if kind == "traversal":
                self.ledger["files"][0]["path"] = "../outside.dll"
            elif kind == "duplicate":
                self.ledger["files"][1] = self.ledger["files"][0]
            else:
                self.ledger["files"][0]["backup"] = "../../outside.dll"
            self.save()
            before = self.snapshot()
            with self.assertRaisesRegex(ValueError, "Unsafe"):
                restore_native.restore(self.root, True)
            self.assertEqual(self.snapshot(), before)

    def test_symlink_destination_refused(self):
        destination = self.root / restore_native.ALLOWED[1]
        destination.unlink()
        destination.symlink_to(self.root / restore_native.ALLOWED[0])
        with self.assertRaisesRegex(ValueError, "symlink"):
            restore_native.restore(self.root, True)

    @unittest.skipUnless(os.environ.get("RISKOFVR_PWSH"), "Set RISKOFVR_PWSH for cross-language ledger test")
    def test_actual_csharp_ledger_can_be_restored_by_python(self):
        with tempfile.TemporaryDirectory(prefix="riskofvr-cross-language-") as fixture:
            result = subprocess.run([os.environ["RISKOFVR_PWSH"], "-NoProfile", "-File",
                str(REPO / "tests/runtime_safety/run.ps1"), "-RestoreFixture", fixture], capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            restore_native.restore(fixture, True)
            self.assertEqual((Path(fixture) / restore_native.ALLOWED[0]).read_text(), "cross-language original")
            self.assertFalse((Path(fixture) / restore_native.ALLOWED[1]).exists())
            restore_native.restore(fixture, True)


if __name__ == "__main__":
    unittest.main()
