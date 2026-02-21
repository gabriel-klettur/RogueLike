#!/usr/bin/env python3
"""
Migration: buildings_data.json -> buildings_templates.json + buildings_instances.json

Usage:
  python scripts/migrate_buildings_to_split.py \
      [--input <legacy_path>] \
      [--templates <templates_path>] \
      [--instances <instances_path>] \
      [--force] [--dry-run]

Defaults use engine config paths for input/output.

This script:
- Loads legacy combined buildings JSON (list of entries).
- Deduplicates templates by static signature (assets.idle, solid, split_ratio, collider_scope, original_scale).
- Writes templates with stable incremental IDs.
- Writes instances that reference template_id and carry per-instance overrides: scale, z_top/z_bottom, collider overrides (when CU), z snapshot if present.
- Preserves legacy instance id when possible; otherwise assigns a new incremental one.

Safe to re-run with --force to overwrite outputs.
"""
import argparse
import json
import os
import sys
from typing import Dict, Tuple, Optional

# Import project config for default paths
try:
    from roguelike_engine.config.config import (
        BUILDINGS_DATA_PATH,
        BUILDINGS_TEMPLATES_PATH,
        BUILDINGS_INSTANCES_PATH,
    )
    from roguelike_engine.config.map_config import global_map_settings
except Exception:
    # Fallback defaults if imports fail (script used out of repo context)
    BUILDINGS_DATA_PATH = os.path.join("data", "buildings", "buildings_data.json")
    BUILDINGS_TEMPLATES_PATH = os.path.join("data", "buildings", "buildings_templates.json")
    BUILDINGS_INSTANCES_PATH = os.path.join("data", "buildings", "buildings_instances.json")
    # minimal stub for zone canonicalization
    class _StubGlobalMapSettings:
        zone_offsets: Dict[str, Tuple[int, int]] = {}
    global_map_settings = _StubGlobalMapSettings()  # type: ignore


def _normalize_asset_path(p: Optional[str]) -> Optional[str]:
    try:
        if not p or not isinstance(p, str):
            return p
        q = p.replace("\\", "/")
        while "//" in q:
            q = q.replace("//", "/")
        base, ext = os.path.splitext(q)
        if ext:
            q = f"{base}{ext.lower()}"
        return q
    except Exception:
        return p


def _canonicalize_zone(zone: Optional[str]) -> Optional[str]:
    try:
        if not zone or not isinstance(zone, str):
            return zone
        if zone.lower() == "no zone":
            return "no zone"
        offsets = getattr(global_map_settings, "zone_offsets", {}) or {}
        if zone in offsets:
            return zone
        low = zone.lower()
        if low in ("lobby", "dungeon") and low in offsets:
            return low
        for k in offsets.keys():
            if k.lower() == low:
                return k
        return zone
    except Exception:
        return zone


def _template_signature(entry: dict) -> str:
    """Build a deterministic signature for a template from a legacy entry."""
    try:
        assets = entry.get("assets") or {}
        idle = assets.get("idle") if isinstance(assets, dict) else None
        img = _normalize_asset_path(idle)
        solid = bool(entry.get("solid", True))
        split_ratio = round(float(entry.get("split_ratio", 0.5)), 3)
        collider_scope = entry.get("collider_scope", "CG")
        original_scale = entry.get("original_scale") if isinstance(entry.get("original_scale"), (list, tuple)) else None
        sig = {
            "img": img,
            "solid": solid,
            "split_ratio": split_ratio,
            "collider_scope": collider_scope,
            "original_scale": list(original_scale) if original_scale else None,
        }
        return json.dumps(sig, sort_keys=True, ensure_ascii=False)
    except Exception:
        return json.dumps({"e": "invalid"}, sort_keys=True)


def _build_template_from_entry(entry: dict, tid: int) -> dict:
    assets = entry.get("assets") or {}
    idle = assets.get("idle") if isinstance(assets, dict) else None
    out = {
        "id": int(tid),
        "assets": {"idle": _normalize_asset_path(idle)},
        "solid": bool(entry.get("solid", True)),
        "split_ratio": round(float(entry.get("split_ratio", 0.5)), 3),
        "collider_scope": entry.get("collider_scope", "CG"),
    }
    if isinstance(entry.get("original_scale"), (list, tuple)):
        out["original_scale"] = list(entry["original_scale"])  # type: ignore[index]
    return out


def _build_instance_from_entry(entry: dict, template_id: int, iid: int) -> dict:
    zone = _canonicalize_zone(entry.get("zone"))
    relx = int(entry.get("rel_x", 0))
    rely = int(entry.get("rel_y", 0))

    inst = {
        "id": int(iid),
        "template_id": int(template_id),
        "zone": zone,
        "rel_x": relx,
        "rel_y": rely,
    }
    # Preserve spawn link if present
    if entry.get("spawn_id") is not None:
        inst["spawn_id"] = str(entry.get("spawn_id"))

    overrides = {}
    if isinstance(entry.get("scale"), (list, tuple)):
        overrides["scale"] = [int(entry["scale"][0]), int(entry["scale"][1])]  # type: ignore[index]
    if entry.get("z_bottom") is not None:
        overrides["z_bottom"] = entry.get("z_bottom")
    if entry.get("z_top") is not None:
        overrides["z_top"] = entry.get("z_top")
    if entry.get("collider_scope", "CG") == "CU" and isinstance(entry.get("collision_override"), dict):
        co = entry["collision_override"]
        try:
            overrides["collider_scope"] = "CU"
            overrides["collision_override"] = {
                "width": int(co.get("width", 0)),
                "height": int(co.get("height", 0)),
                "collision": co.get("collision", []),
            }
        except Exception:
            pass
    if entry.get("z") is not None:
        # Keep z snapshot under overrides for parity with saver
        overrides.setdefault("z", entry.get("z"))

    if overrides:
        inst["overrides"] = overrides

    return inst


def migrate(input_path: str, templates_path: str, instances_path: str, force: bool = False, dry_run: bool = False) -> int:
    if not os.path.exists(input_path):
        print(f"[Buildings][Migrate] Input not found: {input_path}")
        return 2

    # Read legacy entries
    try:
        with open(input_path, "r", encoding="utf-8-sig") as rf:
            entries = json.load(rf) or []
        if not isinstance(entries, list):
            print(f"[Buildings][Migrate] Legacy file is not a list: {input_path}")
            return 3
    except Exception as e:
        print(f"[Buildings][Migrate] Failed to read legacy file: {e}")
        return 3

    # Prepare outputs
    out_dir_t = os.path.dirname(templates_path)
    out_dir_i = os.path.dirname(instances_path)
    if out_dir_t:
        os.makedirs(out_dir_t, exist_ok=True)
    if out_dir_i:
        os.makedirs(out_dir_i, exist_ok=True)

    if not force:
        for p in (templates_path, instances_path):
            if os.path.exists(p):
                print(f"[Buildings][Migrate] Output exists: {p} (use --force to overwrite)")
                return 4

    # Build templates mapping and instances list
    sig_to_tid: Dict[str, int] = {}
    templates: Dict[int, dict] = {}
    instances = []

    # Instance id management: preserve if valid/unique; otherwise assign new
    used_iids = set()
    for e in entries:
        pid = e.get("id")
        if pid is not None and str(pid).isdigit():
            used_iids.add(int(pid))
    next_iid = (max(used_iids) + 1) if used_iids else 1

    max_tid = 0

    for e in entries:
        try:
            sig = _template_signature(e)
            tid = sig_to_tid.get(sig)
            if tid is None:
                max_tid += 1
                tid = max_tid
                templates[tid] = _build_template_from_entry(e, tid)
                sig_to_tid[sig] = tid

            # Determine instance id (preserve if possible)
            iid = e.get("id")
            if iid is None or not str(iid).isdigit():
                iid = next_iid
                next_iid += 1
            else:
                iid = int(iid)

            inst = _build_instance_from_entry(e, tid, iid)
            instances.append(inst)
        except Exception as ex:
            print(f"[Buildings][Migrate] Skipping entry due to error: {ex}")
            continue

    # Sort by id
    templates_list = [templates[k] for k in sorted(templates.keys())]
    instances_list = sorted(instances, key=lambda x: int(x.get("id", 0)))

    if dry_run:
        print(f"[Buildings][Migrate] DRY RUN: would write {len(templates_list)} templates and {len(instances_list)} instances.")
        return 0

    try:
        with open(templates_path, "w", encoding="utf-8") as tf:
            json.dump(templates_list, tf, indent=4)
        with open(instances_path, "w", encoding="utf-8") as inf:
            json.dump(instances_list, inf, indent=4)
    except Exception as e:
        print(f"[Buildings][Migrate] Failed to write outputs: {e}")
        return 5

    print(f"[Buildings][Migrate] ✅ Wrote {len(templates_list)} templates -> {templates_path}")
    print(f"[Buildings][Migrate] ✅ Wrote {len(instances_list)} instances -> {instances_path}")
    return 0


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description="Migrate legacy buildings_data.json to split templates/instances")
    parser.add_argument("--input", dest="input_path", default=BUILDINGS_DATA_PATH)
    parser.add_argument("--templates", dest="templates_path", default=BUILDINGS_TEMPLATES_PATH)
    parser.add_argument("--instances", dest="instances_path", default=BUILDINGS_INSTANCES_PATH)
    parser.add_argument("--force", action="store_true", help="Overwrite outputs if they exist")
    parser.add_argument("--dry-run", action="store_true", help="Do not write files; just report")

    args = parser.parse_args(argv)
    return migrate(args.input_path, args.templates_path, args.instances_path, force=args.force, dry_run=args.dry_run)


if __name__ == "__main__":
    sys.exit(main())
