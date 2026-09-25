"""Fail-closed tests for the V3 migration wrapper's split connection formats."""
import contextlib
import importlib.util
import io
import os
import pathlib
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch


ROOT = pathlib.Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location(
    "run_v3_migrations", ROOT / "tools/ops/run-v3-migrations.py"
)
runner = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(runner)


class MigrationWrapperTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory(prefix="v3-migration-wrapper-")
        self.addCleanup(self.directory.cleanup)
        self.bundle = pathlib.Path(self.directory.name) / "efbundle"
        self.bundle.write_text("#!/bin/sh\nexit 0\n")
        self.bundle.chmod(0o700)
        self.connection = (
            "Host=weymela-v3-pilot-postgres;Port=5432;"
            "Database=weymela_v3_pilot;Username=weymela_v3_migrator;"
            "Passfile=/run/secrets/v3-migrator.pgpass;Include Error Detail=false"
        )

    def run_main(self, connection=None, probe_stdout="weymela_v3_migrator|weymela_v3_migrator\n",
                 probe_returncode=0, bundle_returncode=0, side_effect=None):
        calls = []
        self.last_calls = calls

        def fake_run(command, **kwargs):
            calls.append((command, kwargs))
            if side_effect is not None:
                raise side_effect
            if command[0] == "psql":
                return subprocess.CompletedProcess(command, probe_returncode, probe_stdout, "")
            return subprocess.CompletedProcess(command, bundle_returncode, "", "")

        with patch.dict(os.environ, {"WEYMELA_V3_MIGRATOR_CONNECTION": connection or self.connection}, clear=False), \
             patch.object(sys, "argv", ["run-v3-migrations.py", str(self.bundle)]), \
             patch.object(runner.subprocess, "run", side_effect=fake_run):
            result = runner.main()
        return result, calls

    def assert_refused(self, connection, expected=None):
        with self.assertRaises(SystemExit) as raised:
            self.run_main(connection)
        if expected:
            self.assertIn(expected, str(raised.exception))

    def test_valid_npgsql_string_is_accepted_and_original_reaches_bundle(self):
        result, calls = self.run_main()
        self.assertEqual(result, 0)
        self.assertEqual(len(calls), 2)
        self.assertEqual(calls[0][0][0], "psql")
        self.assertEqual(calls[0][0][-1], "SELECT current_user, session_user;")
        self.assertEqual(calls[0][1]["env"]["PGPASSFILE"], "/run/secrets/v3-migrator.pgpass")
        self.assertNotIn("PGPASSWORD", calls[0][1]["env"])
        self.assertEqual(calls[1][0], [str(self.bundle), "--connection", self.connection])

    def test_probe_receives_explicit_libpq_parameters(self):
        _, calls = self.run_main()
        probe = calls[0][0]
        self.assertIn(("--host", "weymela-v3-pilot-postgres"), tuple(zip(probe, probe[1:])))
        self.assertIn(("--port", "5432"), tuple(zip(probe, probe[1:])))
        self.assertIn(("--username", "weymela_v3_migrator"), tuple(zip(probe, probe[1:])))
        self.assertIn(("--dbname", "weymela_v3_pilot"), tuple(zip(probe, probe[1:])))
        self.assertNotIn(self.connection, probe)

    def test_pilot_style_connection_string_succeeds_through_mocked_probe(self):
        result, calls = self.run_main(
            "Host=weymela-v3-pilot-postgres;Database=weymela_v3_pilot;"
            "Username=weymela_v3_migrator;Passfile=/run/secrets/v3-migrator.pgpass;"
            "Include Error Detail=false"
        )
        self.assertEqual(result, 0)
        self.assertEqual(calls[0][0][-1], "SELECT current_user, session_user;")

    def test_bootstrap_identity_is_rejected(self):
        self.assert_refused(self.connection.replace("weymela_v3_migrator", "weymela_v3_bootstrap"), "Username")

    def test_wrong_current_user_is_rejected(self):
        with self.assertRaises(SystemExit):
            self.run_main(probe_stdout="weymela_v3_bootstrap|weymela_v3_migrator\n")

    def test_wrong_session_user_is_rejected(self):
        with self.assertRaises(SystemExit):
            self.run_main(probe_stdout="weymela_v3_migrator|weymela_v3_bootstrap\n")

    def test_missing_passfile_is_rejected(self):
        self.assert_refused(self.connection.replace(";Passfile=/run/secrets/v3-migrator.pgpass", ""), "Passfile")

    def test_password_field_is_rejected_without_echoing_secret(self):
        secret = "super-secret-fixture-value"
        with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
            with self.assertRaises(SystemExit) as raised:
                self.run_main(self.connection.replace(";Include Error Detail=false", f";Password={secret}"))
        self.assertNotIn(secret, str(raised.exception))

    def test_malformed_and_unsupported_connection_strings_are_rejected(self):
        for value in (
            "Host=weymela-v3-pilot-postgres;Database",
            self.connection.replace(";Include Error Detail=false", ";Host=duplicate"),
            self.connection.replace(";Include Error Detail=false", ";Ssl Mode=Require"),
            self.connection.replace("Passfile=/run/secrets/v3-migrator.pgpass", "Passfile='unterminated"),
        ):
            with self.subTest(value=value):
                with self.assertRaises(SystemExit):
                    self.run_main(value)

    def test_probe_failure_prevents_bundle_execution(self):
        with self.assertRaises(SystemExit):
            self.run_main(probe_returncode=1)
        self.assertEqual(len(self.last_calls), 1)
        self.assertEqual(self.last_calls[0][0][0], "psql")

    def test_unexpected_probe_output_prevents_bundle_execution(self):
        with self.assertRaises(SystemExit):
            self.run_main(probe_stdout="weymela_v3_migrator|weymela_v3_migrator\nextra\n")
        self.assertEqual(len(self.last_calls), 1)

    def test_probe_timeout_prevents_bundle_execution(self):
        with self.assertRaises(SystemExit):
            self.run_main(side_effect=subprocess.TimeoutExpired("psql", 30))
        self.assertEqual(len(self.last_calls), 1)

    def test_values_cannot_inject_shell_arguments_or_commands(self):
        malicious_host = "pilot.example;--dbname=other;$(touch /tmp/not-created)"
        connection = self.connection.replace("weymela-v3-pilot-postgres", f"'{malicious_host}'")
        result, calls = self.run_main(connection)
        self.assertEqual(result, 0)
        probe = calls[0][0]
        self.assertIn(malicious_host, probe)
        self.assertEqual(sum(value == malicious_host for value in probe), 1)
        self.assertFalse(any("touch" == value for value in probe))
        self.assertNotIn("shell", calls[0][1])

    def test_secrets_are_not_printed_by_refusal(self):
        secret_path = "/run/secrets/very-private-passfile"
        output = io.StringIO()
        with contextlib.redirect_stdout(output), contextlib.redirect_stderr(output):
            with self.assertRaises(SystemExit):
                self.run_main(self.connection.replace("Passfile=/run/secrets/v3-migrator.pgpass", secret_path + ";Unknown=x"))
        self.assertNotIn(secret_path, output.getvalue())


if __name__ == "__main__":
    unittest.main()
