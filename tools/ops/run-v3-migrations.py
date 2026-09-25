#!/usr/bin/env python3
"""Fail-closed runner for the reviewed V3 EF migration bundle."""
from __future__ import annotations

import argparse
import os
import pathlib
import subprocess
import sys
from typing import NoReturn


MIGRATOR = "weymela_v3_migrator"
SUPPORTED_FIELDS = {
    "host",
    "port",
    "database",
    "username",
    "passfile",
    "include error detail",
}


def fail(message: str) -> NoReturn:
    raise SystemExit(f"V3 migration refused: {message}")


def _normalise_key(value: str) -> str:
    return " ".join(value.split()).casefold()


def _reject_unsafe_value(value: str) -> None:
    if not value or any(ord(character) < 32 or ord(character) == 127 for character in value):
        fail("connection setting values must be non-empty and free of control characters")


def _parse_npgsql_fields(value: str) -> dict[str, str]:
    settings: dict[str, str] = {}
    position = 0
    length = len(value)

    while position < length:
        while position < length and value[position].isspace():
            position += 1
        if position >= length:
            break

        key_start = position
        while position < length and value[position] not in "=;":
            position += 1
        if position >= length or value[position] != "=":
            fail("connection settings must be key=value pairs")
        key = _normalise_key(value[key_start:position])
        if not key:
            fail("connection settings must not contain duplicate or empty keys")
        if key in settings:
            fail("connection settings must not contain duplicate or empty keys")
        if key not in SUPPORTED_FIELDS:
            if key == "password":
                fail("password-bearing connection strings are forbidden; use Passfile")
            fail("unsupported connection setting")

        position += 1
        while position < length and value[position].isspace():
            position += 1
        if position >= length:
            fail("connection setting values must be non-empty")

        if value[position] in "'\"":
            quote = value[position]
            position += 1
            characters: list[str] = []
            closed = False
            while position < length:
                character = value[position]
                if character == "\\" and position + 1 < length:
                    characters.append(value[position + 1])
                    position += 2
                    continue
                if character == quote:
                    if position + 1 < length and value[position + 1] == quote:
                        characters.append(quote)
                        position += 2
                        continue
                    position += 1
                    closed = True
                    break
                characters.append(character)
                position += 1
            if not closed:
                fail("connection setting contains an unterminated quoted value")
            setting = "".join(characters)
            while position < length and value[position].isspace():
                position += 1
            if position < length and value[position] != ";":
                fail("connection settings must be separated by semicolons")
        else:
            setting_start = position
            while position < length and value[position] != ";":
                position += 1
            setting = value[setting_start:position].strip()

        _reject_unsafe_value(setting)
        settings[key] = setting
        if position < length:
            position += 1

    return settings


def parse_connection(value: str) -> dict[str, str]:
    if not value or not value.strip():
        fail("connection string is required")
    settings = _parse_npgsql_fields(value)
    for required in ("host", "database", "username", "passfile"):
        if not settings.get(required):
            fail(f"connection {required.title()} is required")
    if settings["username"] != MIGRATOR:
        fail(f"connection Username must be {MIGRATOR}")
    if "password" in settings:
        fail("password-bearing connection strings are forbidden; use Passfile")
    if not pathlib.PurePath(settings["passfile"]).is_absolute():
        fail("connection Passfile must be an absolute path")
    if "port" in settings:
        try:
            port = int(settings["port"])
        except ValueError:
            fail("connection Port must be a valid TCP port")
        if not 1 <= port <= 65535:
            fail("connection Port must be a valid TCP port")
    return settings


def build_probe(settings: dict[str, str]) -> tuple[list[str], dict[str, str]]:
    command = [
        "psql",
        "-X",
        "-w",
        "-v",
        "ON_ERROR_STOP=1",
        "--host",
        settings["host"],
    ]
    if "port" in settings:
        command.extend(("--port", settings["port"]))
    command.extend(
        (
            "--username",
            settings["username"],
            "--dbname",
            settings["database"],
            "-Atc",
            "SELECT current_user, session_user;",
        )
    )
    # Remove ambient libpq settings, especially password-bearing overrides,
    # and supply only the already-validated protected passfile.
    environment = {
        "PATH": os.environ.get("PATH", ""),
        "PGPASSFILE": settings["passfile"],
    }
    return command, environment


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("bundle", type=pathlib.Path)
    parser.add_argument(
        "--connection-env",
        default="WEYMELA_V3_MIGRATOR_CONNECTION",
        help="environment variable containing a password-free Npgsql connection string",
    )
    args = parser.parse_args()

    bundle = args.bundle.resolve()
    if not bundle.is_file() or not os.access(bundle, os.X_OK):
        fail("migration bundle is missing or not executable")

    connection = os.environ.get(args.connection_env, "")
    if not connection:
        fail(f"{args.connection_env} is required")
    settings = parse_connection(connection)
    probe_command, probe_environment = build_probe(settings)

    try:
        probe = subprocess.run(
            probe_command,
            env=probe_environment,
            stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL,
            text=True,
            check=False,
            timeout=30,
        )
    except (OSError, subprocess.TimeoutExpired):
        fail("database identity probe failed")
    expected = f"{MIGRATOR}|{MIGRATOR}"
    if probe.returncode != 0 or probe.stdout not in (expected + "\n", expected + "\r\n"):
        fail("database session is not directly authenticated as the V3 migrator")

    return subprocess.run(
        [str(bundle), "--connection", connection],
        check=False,
    ).returncode


if __name__ == "__main__":
    sys.exit(main())
