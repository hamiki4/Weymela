#!/usr/bin/env python3
"""Create and verify a paired, encrypted V3 database/receipt backup set.

The caller must stop V3 writers before taking the PostgreSQL dump and keep them
stopped until this tool finishes. This is an operator tool, not a scheduler.
"""

import argparse
import datetime as dt
import hashlib
import io
import json
import os
import pathlib
import re
import shutil
import stat
import subprocess
import sys
import tarfile
import tempfile


RECEIPT_NAME = re.compile(r"r_[0-9a-f]{64}\Z")
FINGERPRINT = re.compile(r"[0-9A-Fa-f]{40,64}\Z")
REMOTE_COMPONENT = re.compile(r"[A-Za-z0-9._/-]+\Z")
REMOTE_IDENTITY = re.compile(r"[A-Za-z0-9._-]+\Z")
EXPECTED_UID = EXPECTED_GID = 1654


def fail(message):
    raise ValueError(message)


def metadata(path, *, directory=False, owner=None, mode=None):
    info = path.lstat()
    if not (stat.S_ISDIR(info.st_mode) if directory else stat.S_ISREG(info.st_mode)):
        fail("Expected a regular, non-symlink file or directory")
    if owner is not None and (info.st_uid, info.st_gid) != owner:
        fail("Receipt owner does not match the API identity")
    if mode is not None and stat.S_IMODE(info.st_mode) != mode:
        fail("Protected file or directory mode is incorrect")
    return info


def digest(path):
    result = hashlib.sha256()
    with path.open("rb") as source:
        for block in iter(lambda: source.read(1024 * 1024), b""):
            result.update(block)
    return result.hexdigest()


def inspect_receipts(source, fixture):
    metadata(source, directory=True, owner=None if fixture else (EXPECTED_UID, EXPECTED_GID), mode=0o700)
    files = []
    for path in sorted(source.iterdir()):
        if not RECEIPT_NAME.fullmatch(path.name):
            fail("Receipt directory contains an unexpected entry; freeze and investigate")
        info = metadata(path, owner=None if fixture else (EXPECTED_UID, EXPECTED_GID), mode=0o600)
        if not 1 <= info.st_size <= 4 * 1024 * 1024:
            fail("Receipt size is outside the supported range")
        files.append({"name": path.name, "bytes": info.st_size, "sha256": digest(path)})
    return files


def check_gpg_key(home, fingerprint):
    metadata(home, directory=True, mode=0o700)
    if not FINGERPRINT.fullmatch(fingerprint):
        fail("Expected a full approved GPG fingerprint")
    result = subprocess.run(
        ["gpg", "--homedir", str(home), "--batch", "--with-colons", "--fingerprint", "--list-keys", fingerprint],
        capture_output=True, text=True, check=False,
    )
    if result.returncode or fingerprint.upper() not in [line.split(":")[9].upper()
                                                       for line in result.stdout.splitlines() if line.startswith("fpr:")]:
        fail("Approved encryption public key is unavailable")


def check_pg_dump(path, fixture):
    metadata(path, mode=None if fixture else 0o600)
    if fixture:
        return
    with path.open("rb") as source:
        if source.read(5) != b"PGDMP":
            fail("Database backup is not a PostgreSQL custom-format dump")
    if shutil.which("pg_restore") is None:
        fail("PostgreSQL pg_restore is required to validate the paired database dump")
    result = subprocess.run(["pg_restore", "--list", str(path)], stdout=subprocess.DEVNULL,
                            stderr=subprocess.DEVNULL, check=False)
    if result.returncode:
        fail("PostgreSQL backup list validation failed")


def safe_remote(args):
    if not (REMOTE_IDENTITY.fullmatch(args.offhost_user or "")
            and REMOTE_IDENTITY.fullmatch(args.offhost_host or "")
            and not args.offhost_user.startswith("-")
            and not args.offhost_host.startswith("-")
            and (args.offhost_dir or "").startswith("/")
            and REMOTE_COMPONENT.fullmatch(args.offhost_dir or "")
            and ".." not in pathlib.PurePosixPath(args.offhost_dir).parts
            and args.environment in pathlib.PurePosixPath(args.offhost_dir).parts):
        fail("An approved private off-host SSH destination is required")
    for path in (args.ssh_key, args.known_hosts):
        if path is None:
            fail("Pinned SSH host keys and a protected SSH identity are required")
        metadata(path)
    if stat.S_IMODE(args.ssh_key.stat().st_mode) != 0o600:
        fail("SSH identity must have mode 0600")


def ssh_options(args):
    return ["-F", "/dev/null", "-o", "BatchMode=yes", "-o", "StrictHostKeyChecking=yes",
            "-o", f"UserKnownHostsFile={args.known_hosts}", "-o", "GlobalKnownHostsFile=/dev/null",
            "-o", "UpdateHostKeys=no", "-o", "IdentitiesOnly=yes", "-o", "ConnectTimeout=10",
            "-o", "LogLevel=ERROR", "-i", str(args.ssh_key)]


def offhost_copy(args, archive, checksum):
    safe_remote(args)
    options = ssh_options(args)
    remote = f"{args.offhost_user}@{args.offhost_host}:{args.offhost_dir.rstrip('/')}/{archive.name}"
    if not REMOTE_COMPONENT.fullmatch(str(archive)):
        fail("Backup output path must use simple path characters for off-host copy")
    result = subprocess.run(["scp", *options, str(archive), remote], stdout=subprocess.DEVNULL,
                            stderr=subprocess.DEVNULL, check=False)
    if result.returncode:
        fail("Encrypted off-host upload failed")
    with tempfile.TemporaryDirectory(prefix="v3-receipt-upload-check-") as temporary:
        downloaded = pathlib.Path(temporary) / archive.name
        result = subprocess.run(["scp", *options, remote, str(downloaded)], stdout=subprocess.DEVNULL,
                                stderr=subprocess.DEVNULL, check=False)
        if result.returncode or digest(downloaded) != checksum:
            fail("Encrypted off-host read-back checksum failed")


def backup(args):
    if not args.fixture_local_only and not args.writers_stopped:
        fail("Stop V3 API and Worker before the paired database/receipt backup")
    source, db_dump, out = args.receipts, args.db_dump, args.output_dir
    if not args.fixture_local_only and source != pathlib.Path(f"/var/lib/weymela-v3/{args.environment}/receipts"):
        fail("Receipt source is not the approved environment-specific host directory")
    if not args.fixture_local_only and source.resolve() != source:
        fail("Receipt source path contains a symlink")
    check_pg_dump(db_dump, args.fixture_local_only)
    files = inspect_receipts(source, args.fixture_local_only)
    check_gpg_key(args.gpg_home, args.recipient)
    metadata(out, directory=True, mode=0o700)
    name = f"{args.environment}-{dt.datetime.now(dt.timezone.utc):%Y%m%dT%H%M%SZ}-{os.urandom(6).hex()}.receiptset.gpg"
    archive = out / name
    manifest = {"formatVersion": 1, "environment": args.environment,
                "createdAtUtc": dt.datetime.now(dt.timezone.utc).isoformat(),
                "fixtureOnly": args.fixture_local_only,
                "database": {"name": db_dump.name, "bytes": db_dump.stat().st_size, "sha256": digest(db_dump)},
                "receipts": files}
    data = json.dumps(manifest, sort_keys=True, separators=(",", ":")).encode()
    fd = os.open(archive, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    try:
        with os.fdopen(fd, "wb") as encrypted:
            process = subprocess.Popen(["gpg", "--homedir", str(args.gpg_home), "--batch", "--no-tty",
                                        "--quiet", "--trust-model", "always", "--recipient", args.recipient,
                                        "--encrypt"], stdin=subprocess.PIPE, stdout=encrypted, stderr=subprocess.PIPE)
            try:
                with tarfile.open(fileobj=process.stdin, mode="w|") as tar:
                    info = tarfile.TarInfo("manifest.json")
                    info.size, info.mode = len(data), 0o600
                    tar.addfile(info, io.BytesIO(data))
                    for path, member in [(db_dump, "database.dump"), *[(source / item["name"], "receipts/" + item["name"]) for item in files]]:
                        info = tarfile.TarInfo(member)
                        info.size, info.mode = path.stat().st_size, 0o600
                        info.uid = info.gid = EXPECTED_UID if member.startswith("receipts/") else 0
                        with path.open("rb") as stream:
                            tar.addfile(info, stream)
                process.stdin.close()
                error = process.stderr.read()
                if process.wait() != 0:
                    fail("GPG encryption failed")
            except Exception:
                process.kill()
                process.wait()
                raise
            finally:
                process.stderr.close()
    except Exception:
        archive.unlink(missing_ok=True)
        raise
    if digest(db_dump) != manifest["database"]["sha256"] or inspect_receipts(source, args.fixture_local_only) != files:
        archive.unlink(missing_ok=True)
        fail("Source changed during backup; discard this backup set")
    checksum = digest(archive)
    sidecar = out / (name + ".sha256")
    fd = os.open(sidecar, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    with os.fdopen(fd, "w") as target:
        target.write(f"{checksum}  {name}\n")
    if not args.fixture_local_only:
        offhost_copy(args, archive, checksum)
        remote_sidecar = f"{args.offhost_user}@{args.offhost_host}:{args.offhost_dir.rstrip('/')}/{sidecar.name}"
        options = ssh_options(args)
        if subprocess.run(["scp", *options, str(sidecar), remote_sidecar], stdout=subprocess.DEVNULL,
                          stderr=subprocess.DEVNULL, check=False).returncode:
            fail("Off-host checksum upload failed")
    print(json.dumps({"backup": str(archive), "bytes": archive.stat().st_size,
                      "sha256": checksum, "receiptCount": len(files),
                      "offhostVerified": not args.fixture_local_only, "fixtureOnly": args.fixture_local_only}))


def verify_restore(args):
    archive, destination = args.archive, args.restore_dir
    if not re.fullmatch(r"[0-9a-fA-F]{64}", args.expected_sha256):
        fail("Expected a full SHA-256 checksum")
    metadata(archive, mode=0o600)
    if digest(archive) != args.expected_sha256:
        fail("Encrypted backup checksum mismatch")
    resolved = destination.resolve()
    if str(resolved).startswith(("/var/lib/weymela-v3/", "/etc/weymela-v3/")):
        fail("Verification restore cannot target a live Weymela directory")
    if destination.exists() or destination.is_symlink():
        fail("Verification restore requires a new empty directory")
    destination.mkdir(mode=0o700)
    receipts = destination / "receipts"
    receipts.mkdir(mode=0o700)
    os.chown(receipts, EXPECTED_UID, EXPECTED_GID)
    process = subprocess.Popen(["gpg", "--homedir", str(args.gpg_home), "--batch", "--no-tty",
                                "--quiet", "--decrypt", str(archive)], stdout=subprocess.PIPE,
                               stderr=subprocess.PIPE)
    observed = {}
    manifest = None
    try:
        with tarfile.open(fileobj=process.stdout, mode="r|") as tar:
            for member in tar:
                if not member.isfile() or member.name in observed or member.size < 0:
                    fail("Backup contains an unsafe or duplicate member")
                if member.name == "manifest.json":
                    if member.size > 10 * 1024 * 1024 or manifest is not None:
                        fail("Backup manifest is invalid")
                    manifest = json.load(tar.extractfile(member))
                    observed[member.name] = True
                    continue
                if member.name != "database.dump" and not (member.name.startswith("receipts/")
                                                                and RECEIPT_NAME.fullmatch(member.name[9:])):
                    fail("Backup contains an unexpected path")
                if member.name.startswith("receipts/") and member.size > 4 * 1024 * 1024:
                    fail("Backup receipt exceeds the supported size")
                target = destination / member.name
                fd = os.open(target, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
                hashed = hashlib.sha256()
                size = 0
                with os.fdopen(fd, "wb") as output, tar.extractfile(member) as source:
                    for block in iter(lambda: source.read(1024 * 1024), b""):
                        output.write(block)
                        hashed.update(block)
                        size += len(block)
                if size != member.size:
                    fail("Backup member length mismatch")
                if member.name.startswith("receipts/"):
                    os.chown(target, EXPECTED_UID, EXPECTED_GID)
                observed[member.name] = {"bytes": size, "sha256": hashed.hexdigest()}
        process.stdout.close()
        if process.wait() != 0:
            fail("GPG decryption failed")
    except Exception:
        process.kill()
        process.wait()
        raise
    finally:
        process.stderr.close()
    if not isinstance(manifest, dict) or manifest.get("formatVersion") != 1:
        fail("Backup manifest missing or unsupported")
    if not isinstance(manifest.get("receipts"), list):
        fail("Backup manifest receipt inventory is invalid")
    expected = {"manifest.json", "database.dump"}
    for item in manifest["receipts"]:
        if not isinstance(item, dict) or not RECEIPT_NAME.fullmatch(item.get("name", "")):
            fail("Backup manifest contains an invalid receipt key")
        if "receipts/" + item["name"] in expected:
            fail("Backup manifest repeats a receipt key")
        expected.add("receipts/" + item["name"])
        if observed.get("receipts/" + item["name"]) != {"bytes": item["bytes"], "sha256": item["sha256"]}:
            fail("Restored receipt checksum mismatch")
        metadata(receipts / item["name"], owner=(EXPECTED_UID, EXPECTED_GID), mode=0o600)
    if set(observed) != expected or observed["database.dump"] != {"bytes": manifest["database"]["bytes"],
                                                                  "sha256": manifest["database"]["sha256"]}:
        fail("Restored database or member inventory mismatch")
    metadata(receipts, directory=True, owner=(EXPECTED_UID, EXPECTED_GID), mode=0o700)
    print(json.dumps({"verified": True, "environment": manifest["environment"],
                      "receiptCount": len(manifest["receipts"]), "databaseSha256": manifest["database"]["sha256"],
                      "fixtureOnly": manifest["fixtureOnly"], "restoreDir": str(destination)}))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    make = commands.add_parser("backup")
    make.add_argument("--environment", choices=["pilot", "production"], required=True)
    make.add_argument("--receipts", type=pathlib.Path, required=True)
    make.add_argument("--db-dump", type=pathlib.Path, required=True)
    make.add_argument("--output-dir", type=pathlib.Path, required=True)
    make.add_argument("--gpg-home", type=pathlib.Path, required=True)
    make.add_argument("--recipient", required=True)
    make.add_argument("--writers-stopped", action="store_true")
    make.add_argument("--fixture-local-only", action="store_true", help="isolated fixture test; never an off-host backup")
    make.add_argument("--offhost-user")
    make.add_argument("--offhost-host")
    make.add_argument("--offhost-dir")
    make.add_argument("--ssh-key", type=pathlib.Path)
    make.add_argument("--known-hosts", type=pathlib.Path)
    restore = commands.add_parser("verify-restore")
    restore.add_argument("--archive", type=pathlib.Path, required=True)
    restore.add_argument("--expected-sha256", required=True)
    restore.add_argument("--restore-dir", type=pathlib.Path, required=True)
    restore.add_argument("--gpg-home", type=pathlib.Path, required=True)
    args = parser.parse_args()
    try:
        (backup if args.command == "backup" else verify_restore)(args)
    except (ValueError, OSError, KeyError, TypeError, json.JSONDecodeError, tarfile.TarError) as error:
        print(f"Receipt backup refused: {type(error).__name__}: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
