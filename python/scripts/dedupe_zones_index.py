#!/usr/bin/env python3
"""Utility to normalize and deduplicate zones.json for the active world.

This script applies the same "one zone per offset" invariant enforced at
runtime by MapSettings, but writes back a cleaned zones.json on disk.

Behaviour:
  * Groups all zones by logical offset (x, y) as stored in zones.json.
  * For each offset, keeps a single zone name using the same heuristics as
    MapSettings._dedupe_zone_offsets and drops the rest.
  * Optionally performs a dry-run to show what *would* change.
  * Creates a backup copy of the original file by default.

The active world and base path are resolved via global_map_settings, so this
respects current configuration (DATA_DIR, worlds, etc.).
"""

from __future__ import annotations

import argparse
import json
import logging
import os
import sys
from pathlib import Path
from typing import Dict, List, Tuple

LOG = logging.getLogger(__name__)


def ensure_src_on_path() -> None:
    """Ensure the local src/ folder is on sys.path.

    This mirrors the behaviour of other CLI helpers in this repository so
    the script can be executed directly with `python scripts/dedupe_zones_index.py`.
    """

    here = os.path.dirname(os.path.abspath(__file__))
    repo_root = os.path.abspath(os.path.join(here, os.pardir))
    src_dir = os.path.join(repo_root, "src")
    if src_dir not in sys.path:
        sys.path.insert(0, src_dir)


def load_raw_zones(index_path: Path) -> Dict[str, List[int]]:
    if not index_path.is_file():
        raise FileNotFoundError(f"zones.json not found: {index_path}")
    text = index_path.read_text(encoding="utf-8").strip()
    if not text:
        return {}
    data = json.loads(text)
    if not isinstance(data, dict):
        raise ValueError(f"Expected dict at {index_path}, got {type(data)!r}")
    # We keep the raw list[int] representation here to stay close to the
    # on-disk format; conversion to tuples happens later.
    result: Dict[str, List[int]] = {}
    for key, value in data.items():
        if not isinstance(value, (list, tuple)) or len(value) != 2:
            LOG.warning("Skipping zone %r with invalid offset %r", key, value)
            continue
        try:
            x = int(value[0])
            y = int(value[1])
        except Exception as exc:  # pragma: no cover - extremely defensive
            LOG.warning("Skipping zone %r with non-integer offset %r: %s", key, value, exc)
            continue
        result[str(key)] = [x, y]
    return result


def compute_deduplication(
    raw: Dict[str, List[int]],
) -> Tuple[Dict[str, Tuple[int, int]], List[str], List[Tuple[Tuple[int, int], str, List[str]]]]:
    """Compute a deduplicated view and grouping report from raw JSON data.

    Returns a triple:
      - dedup: mapping name -> (x, y) *after* deduplication (no duplicates).
      - sentinels: list of keys in raw considered sentinel zones ("no zone", "no-zone").
      - groups: list of (offset, chosen_name, dropped_names) for reporting.
    """

    ensure_src_on_path()
    from roguelike_engine.config.map_config import global_map_settings

    # Separate sentinel-style names; they should not participate in dedup
    # heuristic as they are injected by MapSettings at runtime anyway.
    sentinels: List[str] = []
    offsets: Dict[str, Tuple[int, int]] = {}
    for name, values in raw.items():
        low = str(name).lower()
        if low in ("no zone", "no-zone"):
            sentinels.append(name)
            continue
        x, y = int(values[0]), int(values[1])
        offsets[name] = (x, y)

    # Use the runtime heuristic to decide which zone name to keep per offset.
    dedup = global_map_settings._dedupe_zone_offsets(dict(offsets))  # type: ignore[attr-defined]

    # Build grouping information for a human-friendly report.
    by_coord: Dict[Tuple[int, int], List[str]] = {}
    for name, off in offsets.items():
        by_coord.setdefault(off, []).append(name)

    groups: List[Tuple[Tuple[int, int], str, List[str]]] = []
    for off, names in by_coord.items():
        winners = [n for n in names if dedup.get(n) == off]
        if winners:
            chosen = winners[0]
        else:
            # Extremely defensive: if, for any reason, the helper did not
            # preserve any of the original names for this offset, fallback to
            # the first one.
            chosen = names[0]
        dropped = [n for n in names if n != chosen]
        groups.append((off, chosen, dropped))

    return dedup, sentinels, groups


def write_clean_zones(
    index_path: Path,
    raw: Dict[str, List[int]],
    dedup: Dict[str, Tuple[int, int]],
    sentinels: List[str],
    *,
    backup: bool = True,
) -> None:
    """Write a cleaned zones.json file.

    The on-disk format is kept as {name: [x, y]}, similar to the original
    zones.json. Sentinel entries present in the original file are preserved
    verbatim, but are not part of the deduplication process.
    """

    # Build a deterministic key order starting from the original file order
    # but skipping any names that were dropped by deduplication.
    ordered_names: List[str] = []
    for name in raw.keys():
        if name in dedup or name in sentinels:
            ordered_names.append(name)
    # Include any additional deduped names that might not have been present
    # in the original (very unlikely, but safe).
    for name in dedup.keys():
        if name not in ordered_names:
            ordered_names.append(name)

    clean: Dict[str, List[int]] = {}
    for name in ordered_names:
        if name in dedup:
            x, y = dedup[name]
            clean[name] = [int(x), int(y)]
        elif name in sentinels:
            clean[name] = list(raw[name])

    if backup and index_path.is_file():
        backup_path = index_path.with_suffix(index_path.suffix + ".bak")
        backup_path.write_text(index_path.read_text(encoding="utf-8"), encoding="utf-8")
        LOG.info("Backup written to %s", backup_path)

    index_path.write_text(json.dumps(clean, ensure_ascii=False, indent=2), encoding="utf-8")
    LOG.info("Cleaned zones.json written to %s", index_path)


def build_arg_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Deduplicate zones.json for the active world.")
    parser.add_argument(
        "--world",
        type=str,
        default=None,
        help=(
            "Optional world id to set on global_map_settings before running. "
            "If omitted, the current_world value is used."
        ),
    )
    parser.add_argument(
        "--index",
        type=str,
        default=None,
        help=(
            "Optional explicit path to a zones.json file. "
            "If provided, overrides global_map_settings.ZONES_INDEX."
        ),
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Only report duplicates; do not modify zones.json.",
    )
    parser.add_argument(
        "--no-backup",
        action="store_true",
        help="Do not create a .bak backup of the original zones.json before writing.",
    )
    parser.add_argument(
        "--verbose",
        "-v",
        action="count",
        default=0,
        help="Increase verbosity (can be used multiple times).",
    )
    return parser


def configure_logging(verbosity: int) -> None:
    level = logging.WARNING
    if verbosity >= 2:
        level = logging.DEBUG
    elif verbosity == 1:
        level = logging.INFO
    logging.basicConfig(level=level, format="[%(levelname)s] %(message)s")


def main(argv: list[str] | None = None) -> int:
    parser = build_arg_parser()
    args = parser.parse_args(argv)
    configure_logging(args.verbose)

    ensure_src_on_path()
    from roguelike_engine.config.map_config import global_map_settings

    if args.world:
        global_map_settings.set_world(args.world)

    index_path: Path
    if args.index:
        index_path = Path(args.index)
    else:
        index_path = global_map_settings.ZONES_INDEX

    LOG.info("Using zones index: %s", index_path)

    try:
        raw = load_raw_zones(index_path)
    except Exception as exc:
        LOG.error("Failed to read zones index %s: %s", index_path, exc)
        return 1

    if not raw:
        LOG.info("zones.json is empty; nothing to deduplicate.")
        return 0

    dedup, sentinels, groups = compute_deduplication(raw)

    # Print a human-readable report of changes
    total_before = len([k for k in raw.keys() if k not in sentinels])
    total_after = len(dedup)
    LOG.info("Zones before dedup (excluding sentinels): %d", total_before)
    LOG.info("Zones after dedup: %d", total_after)

    any_dropped = False
    for (off_x, off_y), chosen, dropped in groups:
        if not dropped:
            continue
        any_dropped = True
        print(f"offset=({off_x},{off_y}): keeping '{chosen}', dropping {dropped}")

    if not any_dropped:
        print("No duplicate offsets detected; zones.json is already normalized.")
        return 0

    if args.dry_run:
        print("Dry run requested; zones.json was NOT modified.")
        return 0

    try:
        write_clean_zones(index_path, raw, dedup, sentinels, backup=not args.no_backup)
    except Exception as exc:
        LOG.error("Failed to write cleaned zones index %s: %s", index_path, exc)
        return 2

    print("zones.json successfully deduplicated.")
    return 0


if __name__ == "__main__":  # pragma: no cover - direct CLI entry point
    raise SystemExit(main())
