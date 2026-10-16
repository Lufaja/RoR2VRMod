#!/usr/bin/env python3
"""Preview or restore VRMod's three native writes; close the game/mod manager first.

The ledger is left disabled after --apply so a still-installed patcher cannot undo
rollback. Backups are retained. No game files outside the fixed allowlist are touched.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import tempfile

ALLOWED = (
    "UnitySubsystems/UnityOpenXR/UnitySubsystemsManifest.json",
    "Plugins/x86_64/UnityOpenXR.dll",
    "Plugins/x86_64/openxr_loader.dll",
)


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest() if path.is_file() else None


def safe_path(root, relative):
    candidate = root
    for part in relative.split("/"):
        if part in ("", ".", "..") or "\\" in part or ":" in part:
            raise ValueError(f"Unsafe path: {relative}")
        candidate /= part
        if candidate.is_symlink():
            raise ValueError(f"Refusing symlink: {candidate}")
    return candidate


def atomic_write(path, data, expected):
    descriptor, temporary = tempfile.mkstemp(prefix=path.name + ".vrmod-", dir=path.parent)
    try:
        with os.fdopen(descriptor, "wb") as stream:
            stream.write(data)
            stream.flush()
            os.fsync(stream.fileno())
        if digest(path) != expected:
            raise ValueError(f"File changed while preparing restore: {path}")
        os.replace(temporary, path)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)


def restore(game_data, apply=False):
    root = Path(game_data).resolve(strict=True)
    ledger_path = safe_path(root, ".vrmod-native/ledger.json")
    ledger_hash = digest(ledger_path)
    ledger = json.loads(ledger_path.read_text(encoding="utf-8-sig"))
    if (ledger.get("version") != 1 or not isinstance(ledger.get("disabled"), bool)
            or not isinstance(ledger.get("files"), list) or len(ledger["files"]) > 3):
        raise ValueError("Unsupported or invalid native ledger")
    plans, seen = [], set()
    for entry in ledger["files"]:
        if not isinstance(entry, dict):
            raise ValueError("Invalid native ledger entry")
        path = entry.get("path")
        if path not in ALLOWED or path in seen or entry.get("backup") != f"{ALLOWED.index(path)}.original":
            raise ValueError(f"Unsafe or duplicate ledger entry: {path}")
        seen.add(path)
        for key in ("original_sha256", "installed_sha256", "pending_sha256"):
            value = entry[key]
            if value is not None and (not isinstance(value, str) or not re.fullmatch(r"[0-9a-f]{64}", value)):
                raise ValueError(f"Invalid {key} for {path}")
        destination = safe_path(root, path)
        if destination.exists() and not destination.is_file():
            raise ValueError(f"Destination is not a regular file: {path}")
        backup = safe_path(root, ".vrmod-native/" + entry["backup"])
        original = entry["original_sha256"]
        if original is not None and digest(backup) != original:
            raise ValueError(f"Missing or modified original backup: {path}")
        current = digest(destination)
        known_hashes = {original, entry["installed_sha256"]}
        if entry["pending_sha256"] is not None:
            known_hashes.add(entry["pending_sha256"])
        # Missing files are allowed only if the original or recorded install was absent.
        if current not in known_hashes:
            raise ValueError(f"Refusing externally changed native file: {path}")
        action = "already original" if current == original else "remove added file" if original is None else "restore original"
        plans.append((entry, destination, backup, current, action))

    # Preflight all entries before making any change. Keep this marker for restart safety.
    if apply:
        ledger["disabled"] = True
        atomic_write(ledger_path, (json.dumps(ledger, indent=2) + "\n").encode(), ledger_hash)
    results = []
    for entry, destination, backup, expected, action in plans:
        if apply and action != "already original":
            # Recheck paths/hashes immediately before each write as well as in preflight.
            safe_path(root, entry["path"])
            safe_path(root, ".vrmod-native/" + entry["backup"])
            if digest(destination) != expected:
                raise ValueError(f"File changed during restore: {entry['path']}")
            if entry["original_sha256"] is None:
                destination.unlink()
            else:
                if digest(backup) != entry["original_sha256"]:
                    raise ValueError(f"Backup changed during restore: {entry['path']}")
                atomic_write(destination, backup.read_bytes(), expected)
        results.append(f"{entry['path']}: {action}")
    return results


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-data", required=True, help="Risk of Rain 2_Data directory (native SteamOS path)")
    parser.add_argument("--apply", action="store_true", help="Restore originals and disable future patcher writes; default is dry-run")
    args = parser.parse_args()
    try:
        for line in restore(args.game_data, args.apply):
            print(line)
        print("Restored; native patcher is disabled by the retained ledger." if args.apply else "Preview only. Close the game and mod manager, then add --apply to restore.")
    except (OSError, ValueError, KeyError, TypeError) as error:
        parser.exit(1, f"Restore refused: {error}\n")


if __name__ == "__main__":
    main()
