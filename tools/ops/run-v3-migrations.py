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


def fail(message: str) -> NoReturn:
    raise SystemExit(f"V3 migration refused: {message}")


def parse_connection(value: str) -> dict[str, str]:
    settings: dict[str, str] = {}
    for raw_part in value.split(";"):
        part = raw_part.strip()
        if not part:
            continue
        if "=" not in part:
            fail("connection settings must be key=value pairs")
        key, setting = part.split("=", 1)
        key = key.strip().lower()
        if not key or key in settings:
            fail("connection settings must not contain duplicate or empty keys")
        settings[key] = setting.strip()
    return settings


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
    if settings.get("username") != MIGRATOR:
        fail(f"connection Username must be {MIGRATOR}")
    if "password" in settings:
        fail("password-bearing connection strings are forbidden; use Passfile")
    if not settings.get("passfile"):
        fail("connection Passfile is required")

    probe = subprocess.run(
        [
            "psql",
            "-X",
            "-v",
            "ON_ERROR_STOP=1",
            "-Atc",
            "SELECT current_user, session_user",
            connection,
        ],
        stdout=subprocess.PIPE,
        stderr=subprocess.DEVNULL,
        text=True,
        check=False,
    )
    if probe.returncode != 0 or probe.stdout.strip() != f"{MIGRATOR}|{MIGRATOR}":
        fail("database session is not directly authenticated as the V3 migrator")

    return subprocess.run(
        [str(bundle), "--connection", connection],
        check=False,
    ).returncode


if __name__ == "__main__":
    sys.exit(main())
