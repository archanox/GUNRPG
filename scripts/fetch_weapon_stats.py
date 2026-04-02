#!/usr/bin/env python3
"""
Fetch weapon stats from the TrueGameData API and write a deterministic balance snapshot.

Endpoints used:
  POST https://www.truegamedata.com/api/weapons/api.php?game=bo7&action=calc_damage_table
  POST https://www.truegamedata.com/api/weapons/api.php?game=bo7&action=calc_stat_summary

Reads weapon names from  data/weapons.json (relative to the repository root).
Writes output to         balances/YYYY-MM-DD.json.

Change detection: compares the new weapons payload (sorted JSON) against the most
recent file already present in balances/.  If the data is identical the script
exits 0 without writing any new file.  If it differs (or no baseline exists) it
writes the snapshot and exits 0.  Any API or I/O error causes a non-zero exit so
the GitHub Actions step is marked failed and the run is visible in the UI.
"""

from __future__ import annotations

import datetime
import glob
import json
import os
import sys
import urllib.error
import urllib.request

# ---------------------------------------------------------------------------
# Configuration
# ---------------------------------------------------------------------------

API_BASE = "https://www.truegamedata.com/api/weapons/api.php"
GAME = "bo7"

# Headers replicate the mobile app used by TrueGameData (matches the curl
# example given in the issue).
_REQUEST_HEADERS = {
    "Accept": "application/json, text/plain, */*",
    "Content-Type": "application/json",
    "Origin": "capacitor://com.truegamedata.app",
    "User-Agent": (
        "Mozilla/5.0 (iPhone; CPU iPhone OS 18_7 like Mac OS X) "
        "AppleWebKit/605.1.15 (KHTML, like Gecko) Mobile/15E148"
    ),
    "Sec-Fetch-Site": "cross-site",
    "Sec-Fetch-Mode": "cors",
    "Sec-Fetch-Dest": "empty",
}

_REQUEST_TIMEOUT = 30  # seconds

# ---------------------------------------------------------------------------
# API helpers
# ---------------------------------------------------------------------------


def _post_api(action: str, weapon: str) -> dict:
    """POST to the TrueGameData API and return the parsed JSON response."""
    url = f"{API_BASE}?game={GAME}&action={action}"
    payload = json.dumps({"weapon": weapon, "attachments": [], "health": "100"}).encode()
    req = urllib.request.Request(url, data=payload, headers=_REQUEST_HEADERS, method="POST")

    try:
        with urllib.request.urlopen(req, timeout=_REQUEST_TIMEOUT) as resp:
            raw = resp.read().decode("utf-8")
    except urllib.error.URLError as exc:
        print(
            f"ERROR: API unreachable for weapon={weapon!r} action={action!r}: {exc}",
            file=sys.stderr,
        )
        sys.exit(1)

    # The API sometimes returns a double-encoded JSON string (a JSON string
    # whose value is itself a JSON string).  Unwrap one layer if needed.
    try:
        data = json.loads(raw)
    except json.JSONDecodeError as exc:
        print(
            f"ERROR: Could not parse API response for weapon={weapon!r} action={action!r}: {exc}",
            file=sys.stderr,
        )
        sys.exit(1)

    if isinstance(data, str):
        try:
            data = json.loads(data)
        except json.JSONDecodeError as exc:
            print(
                f"ERROR: Could not parse inner JSON for weapon={weapon!r} action={action!r}: {exc}",
                file=sys.stderr,
            )
            sys.exit(1)

    return data


# ---------------------------------------------------------------------------
# Extraction helpers
# ---------------------------------------------------------------------------


def _fixed(value: float | int, decimals: int = 2) -> float | int:
    """Round a numeric value to *decimals* places; return int when the result
    is a whole number to keep the JSON compact."""
    rounded = round(float(value), decimals)
    return int(rounded) if rounded == int(rounded) else rounded


def _extract_damage_ranges(damage_table: dict) -> list[dict]:
    """Return a sorted list of per-range, per-body-part damage entries.

    The API returns a list of lists; we use index 0 (base weapon, no
    attachments).  Each entry contains the dropoff distance plus damage per
    body part.
    """
    outer = damage_table.get("damage") or []
    entries: list[dict] = outer[0] if outer else []

    ranges: list[dict] = []
    for entry in entries:
        r: dict = {
            "range_m": _fixed(entry.get("dropoff", 0)),
            "head": _fixed(entry.get("head", 0)),
            "neck": _fixed(entry.get("neck", 0)),
            "chest": _fixed(entry.get("chest", 0)),
            "stomach": _fixed(entry.get("stomach", 0)),
            "upper_arm": _fixed(entry.get("upperarm", 0)),
            "lower_arm": _fixed(entry.get("lowerarm", 0)),
            "upper_leg": _fixed(entry.get("upperleg", 0)),
            "lower_leg": _fixed(entry.get("lowerleg", 0)),
        }
        ranges.append(r)

    return ranges


def _extract_rpm(damage_table: dict) -> int:
    """Extract the final (effective) RPM from the damage table."""
    outer = damage_table.get("damage") or []
    entries: list[dict] = outer[0] if outer else []
    if entries:
        return int(entries[0].get("final_rpm", entries[0].get("base_rpm", 0)))
    return 0


def _extract_summary_stats(summary: dict) -> dict:
    """Extract deterministic stat fields from a calc_stat_summary response.

    Field names are mapped from multiple possible API spellings so the
    extraction remains functional even if the API adjusts its schema.
    """
    result: dict = {}

    def _pick(candidates: list[str], out_key: str, converter=None) -> None:
        for key in candidates:
            val = summary.get(key)
            if val is not None:
                try:
                    result[out_key] = converter(val) if converter else val
                except (ValueError, TypeError):
                    pass
                return

    _pick(["mag_size", "magazine", "ammo_capacity", "mag"], "mag_size", int)
    _pick(
        ["reload_add_time", "reload_time", "reload_ms", "reload_empty", "reload"],
        "reload_ms",
        lambda v: _fixed(v, 0),
    )
    _pick(
        ["ads_time", "ads_ms", "aim_down_sight", "ads"],
        "ads_ms",
        lambda v: _fixed(v, 0),
    )
    _pick(
        ["sprint_out_time", "sprint_to_fire", "stf_time", "sprint_fire"],
        "sprint_to_fire_ms",
        lambda v: _fixed(v, 0),
    )
    _pick(
        ["bullet_velocity", "muzzle_velocity", "velocity"],
        "bullet_velocity",
        lambda v: _fixed(v, 1),
    )
    _pick(
        ["movement_speed", "move_speed", "ms", "walk_speed"],
        "move_speed",
        lambda v: _fixed(v, 4),
    )
    _pick(
        ["ads_movement_speed", "ads_move_speed", "ads_ms_move"],
        "ads_move_speed",
        lambda v: _fixed(v, 4),
    )

    return result


# ---------------------------------------------------------------------------
# Per-weapon fetch
# ---------------------------------------------------------------------------


def fetch_weapon(weapon: str) -> dict:
    """Return a normalised stat dict for *weapon*."""
    print(f"  Fetching damage table for '{weapon}'…")
    damage_table = _post_api("calc_damage_table", weapon)

    print(f"  Fetching stat summary for '{weapon}'…")
    summary = _post_api("calc_stat_summary", weapon)

    stats: dict = {}

    # RPM is reliably present in the damage table.
    rpm = _extract_rpm(damage_table)
    if rpm:
        stats["rpm"] = rpm

    # Summary stats (mag size, reload, ADS, bullet velocity, movement).
    stats.update(_extract_summary_stats(summary))

    # Damage ranges (dropoff distances + per-body-part damage).
    stats["damage_ranges"] = _extract_damage_ranges(damage_table)

    return stats


# ---------------------------------------------------------------------------
# Snapshot helpers
# ---------------------------------------------------------------------------


def _normalise(data: object) -> str:
    """Return a deterministic, sorted JSON string suitable for comparison."""
    return json.dumps(data, sort_keys=True, ensure_ascii=False, separators=(",", ":"))


def _latest_snapshot(balances_dir: str) -> dict | None:
    """Return the parsed content of the most recent snapshot, or *None*."""
    pattern = os.path.join(balances_dir, "????-??-??.json")
    files = sorted(glob.glob(pattern))
    if not files:
        return None
    with open(files[-1], encoding="utf-8") as fh:
        return json.load(fh)


# ---------------------------------------------------------------------------
# Entry point
# ---------------------------------------------------------------------------


def main() -> None:
    repo_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    weapons_file = os.path.join(repo_root, "data", "weapons.json")
    balances_dir = os.path.join(repo_root, "balances")

    # Load weapon list.
    try:
        with open(weapons_file, encoding="utf-8") as fh:
            weapons: list[str] = json.load(fh)
    except (OSError, json.JSONDecodeError) as exc:
        print(f"ERROR: Could not load {weapons_file}: {exc}", file=sys.stderr)
        sys.exit(1)

    if not weapons:
        print("ERROR: Weapon list is empty.", file=sys.stderr)
        sys.exit(1)

    today = datetime.datetime.now(datetime.timezone.utc).date().isoformat()
    print(f"Building balance snapshot for {today} ({len(weapons)} weapon(s))…\n")

    weapons_data: dict[str, dict] = {}
    for weapon in weapons:
        weapons_data[weapon] = fetch_weapon(weapon)
        print()

    snapshot = {
        "version": today,
        "weapons": dict(sorted(weapons_data.items())),
    }

    os.makedirs(balances_dir, exist_ok=True)

    # Change detection: compare weapons payload only (not the version/date).
    latest = _latest_snapshot(balances_dir)
    if latest is not None:
        if _normalise(snapshot["weapons"]) == _normalise(latest.get("weapons", {})):
            print("No changes detected — snapshot matches the most recent file. Nothing to commit.")
            return

    out_path = os.path.join(balances_dir, f"{today}.json")
    with open(out_path, "w", encoding="utf-8") as fh:
        json.dump(snapshot, fh, sort_keys=True, indent=2, ensure_ascii=False)
        fh.write("\n")

    print(f"New snapshot written: {out_path}")


if __name__ == "__main__":
    main()
