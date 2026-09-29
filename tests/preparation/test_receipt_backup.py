"""Isolated encryption/restore proof for the private receipt backup operator tool."""

import hashlib
import importlib.util
import json
import os
import pathlib
import shutil
import stat
import subprocess
import tempfile
import unittest
from types import SimpleNamespace
from unittest import mock


ROOT = pathlib.Path(__file__).resolve().parents[2]
TOOL = ROOT / "tools/ops/receipt-backup.py"


@unittest.skipUnless(shutil.which("gpg"), "GPG is required")
class ReceiptBackupFixtureTests(unittest.TestCase):
    @unittest.skipUnless(os.geteuid() == 0, "restore ownership verification requires a privileged isolated test")
    def test_encrypted_pair_restores_with_verified_files_and_modes(self):
        with tempfile.TemporaryDirectory(prefix="weymela-receipt-backup-fixture-") as temporary:
            base = pathlib.Path(temporary)
            os.chmod(base, 0o700)
            home, receipts, backups = (base / name for name in ("gpg", "receipts", "backups"))
            for path in (home, receipts, backups):
                path.mkdir(mode=0o700)
            subprocess.run(["gpg", "--homedir", str(home), "--batch", "--no-tty", "--quiet",
                            "--pinentry-mode", "loopback", "--passphrase", "", "--quick-generate-key",
                            "Weymela receipt fixture <fixture@example.invalid>", "rsa2048", "encr", "never"],
                           check=True, capture_output=True)
            keys = subprocess.run(["gpg", "--homedir", str(home), "--batch", "--with-colons",
                                   "--fingerprint", "--list-keys"], check=True, capture_output=True, text=True).stdout
            fingerprint = next(line.split(":")[9] for line in keys.splitlines() if line.startswith("fpr:"))
            receipt_name = "r_" + "a" * 64
            receipt_data = b"isolated non-sensitive fixture only"
            (receipts / receipt_name).write_bytes(receipt_data)
            os.chmod(receipts / receipt_name, 0o600)
            db_dump = base / "fixture.dump"
            db_dump.write_bytes(b"fixture database placeholder; never a Pilot snapshot")
            result = subprocess.run(["python3", str(TOOL), "backup", "--environment", "pilot",
                                     "--receipts", str(receipts), "--db-dump", str(db_dump),
                                     "--output-dir", str(backups), "--gpg-home", str(home),
                                     "--recipient", fingerprint, "--fixture-local-only"],
                                    check=True, capture_output=True, text=True)
            record = json.loads(result.stdout)
            archive = pathlib.Path(record["backup"])
            self.assertEqual(hashlib.sha256(archive.read_bytes()).hexdigest(), record["sha256"])
            self.assertNotIn(receipt_data, archive.read_bytes())
            self.assertFalse(record["offhostVerified"])
            self.assertTrue(record["fixtureOnly"])
            self.assertEqual(record["receiptCount"], 1)
            restore = base / "isolated-restore"
            verified = subprocess.run(["python3", str(TOOL), "verify-restore", "--archive", str(archive),
                                       "--expected-sha256", record["sha256"], "--restore-dir", str(restore),
                                       "--gpg-home", str(home)], check=True, capture_output=True, text=True)
            self.assertTrue(json.loads(verified.stdout)["verified"])
            self.assertEqual((restore / "receipts" / receipt_name).read_bytes(), receipt_data)
            self.assertEqual((restore / "database.dump").read_bytes(), db_dump.read_bytes())
            receipt = (restore / "receipts" / receipt_name).stat()
            self.assertEqual((receipt.st_uid, receipt.st_gid), (1654, 1654))
            self.assertEqual(stat.S_IMODE(receipt.st_mode), 0o600)
            directory = (restore / "receipts").stat()
            self.assertEqual((directory.st_uid, directory.st_gid), (1654, 1654))
            self.assertEqual(stat.S_IMODE(directory.st_mode), 0o700)
            tampered = backups / "tampered.receiptset.gpg"
            tampered.write_bytes(archive.read_bytes() + b"tampered")
            os.chmod(tampered, 0o600)
            refused = subprocess.run(["python3", str(TOOL), "verify-restore", "--archive", str(tampered),
                                      "--expected-sha256", record["sha256"], "--restore-dir", str(base / "wrong"),
                                      "--gpg-home", str(home)], capture_output=True, text=True)
            self.assertNotEqual(refused.returncode, 0)
            self.assertFalse((base / "wrong").exists())
            shutil.rmtree(restore)
            self.assertFalse(restore.exists())
            self.assertTrue(archive.exists())
            print(f"fixture encrypted bytes={record['bytes']} sha256={record['sha256']} "
                  "restore=verified receipt-owner=1654:1654 receipt-modes=0700/0600 offhost=no")

    def test_unexpected_receipt_file_refuses_backup(self):
        with tempfile.TemporaryDirectory(prefix="weymela-receipt-refusal-") as temporary:
            base = pathlib.Path(temporary)
            receipts = base / "receipts"
            receipts.mkdir(mode=0o700)
            (receipts / "tmp_incomplete").write_bytes(b"fixture")
            db_dump = base / "fixture.dump"
            db_dump.write_bytes(b"isolated placeholder")
            result = subprocess.run(["python3", str(TOOL), "backup", "--environment", "pilot",
                                     "--receipts", str(receipts), "--db-dump", str(db_dump),
                                     "--output-dir", str(base), "--gpg-home", str(base),
                                     "--recipient", "0" * 40, "--fixture-local-only"],
                                    capture_output=True, text=True)
            self.assertNotEqual(result.returncode, 0)
            self.assertIn("unexpected entry", result.stderr)
            self.assertFalse(list(base.glob("*.receiptset.gpg")))

    def test_real_backup_requires_stopped_writers_and_approved_source(self):
        with tempfile.TemporaryDirectory(prefix="weymela-receipt-guard-") as temporary:
            base = pathlib.Path(temporary)
            receipts = base / "receipts"
            receipts.mkdir(mode=0o700)
            common = ["python3", str(TOOL), "backup", "--environment", "pilot",
                      "--receipts", str(receipts), "--db-dump", str(base / "dump"),
                      "--output-dir", str(base), "--gpg-home", str(base), "--recipient", "0" * 40]
            frozen = subprocess.run(common, capture_output=True, text=True)
            self.assertIn("Stop V3 API and Worker", frozen.stderr)
            wrong_source = subprocess.run([*common, "--writers-stopped"], capture_output=True, text=True)
            self.assertIn("approved environment-specific host directory", wrong_source.stderr)

    def test_offhost_copy_requires_matching_readback(self):
        spec = importlib.util.spec_from_file_location("receipt_backup", TOOL)
        tool = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(tool)
        with tempfile.TemporaryDirectory(prefix="weymela-offhost-fixture-") as temporary:
            base = pathlib.Path(temporary)
            archive = base / "fixture.receiptset.gpg"
            archive.write_bytes(b"encrypted fixture bytes")
            ssh_key, known_hosts = base / "backup-key", base / "known-hosts"
            ssh_key.write_bytes(b"non-sensitive fixture")
            known_hosts.write_bytes(b"non-sensitive fixture")
            os.chmod(ssh_key, 0o600)
            args = SimpleNamespace(environment="pilot", offhost_user="backup", offhost_host="backup.example.invalid",
                                   offhost_dir="/private/pilot", ssh_key=ssh_key, known_hosts=known_hosts)
            with self.assertRaisesRegex(ValueError, "approved private off-host"):
                tool.safe_remote(SimpleNamespace(**{**vars(args), "offhost_dir": "/private/production"}))
            checksum = hashlib.sha256(archive.read_bytes()).hexdigest()
            calls = []

            def transfer(command, **_):
                calls.append(command)
                if len(calls) == 2:
                    shutil.copy2(archive, command[-1])
                return SimpleNamespace(returncode=0)

            with mock.patch.object(tool.subprocess, "run", side_effect=transfer):
                tool.offhost_copy(args, archive, checksum)
            self.assertEqual(len(calls), 2)
            self.assertIn("StrictHostKeyChecking=yes", calls[0])
            self.assertIn("GlobalKnownHostsFile=/dev/null", calls[0])

            calls.clear()

            def corrupt_transfer(command, **_):
                calls.append(command)
                if len(calls) == 2:
                    pathlib.Path(command[-1]).write_bytes(b"different")
                return SimpleNamespace(returncode=0)

            with mock.patch.object(tool.subprocess, "run", side_effect=corrupt_transfer):
                with self.assertRaisesRegex(ValueError, "read-back checksum failed"):
                    tool.offhost_copy(args, archive, checksum)


if __name__ == "__main__":
    unittest.main()
