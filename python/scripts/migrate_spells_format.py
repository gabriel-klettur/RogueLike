#!/usr/bin/env python3
"""
Migration utility: convert data/spells/spells.json from legacy flat format to new nested format.

New structure groups fields as:
- timings: prepare/channel/cooldown durations
- rules: movement/interruptibility/automatic behavior
- constraints: instance limits and overlap
- effect: gameplay effect fields (speed, damage, range, radius, duration, lifetime, etc.)
- vfx: visual effects (sprite, particles, optional preset name)
- meta: leftover unmapped properties for safety

Usage:
  python scripts/migrate_spells_format.py --input data/spells/spells.json --output data/spells/spells.v2.json
  python scripts/migrate_spells_format.py --inplace --backup

Notes:
- This tool is safe-by-default: you must pass --inplace to overwrite.
- Unknown fields are moved under 'meta' to avoid data loss.
- If legacy already contains a 'vfx' object, it will be copied verbatim.
"""
from __future__ import annotations
import argparse
import json
import shutil
from pathlib import Path
from typing import Any, Dict, Tuple

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_INPUT = ROOT / "data" / "spells" / "spells.json"
DEFAULT_OUTPUT = ROOT / "data" / "spells" / "spells.v2.json"

LEGACY_TO_TIMINGS = {
    "prepare_duration": "prepare",
    "channel_duration": "channel",
    "cooldown_duration": "cooldown",
}

RULE_KEYS = {
    "allow_movement",
    "lock_cast_direction",
    "interruptible",
    "automatic",
    "automatic_cast_punish",
}

CONSTRAINT_KEYS = {
    "max_instances",
    "allow_overlap",
}

EFFECT_KEYS = {
    "speed",
    "damage",
    "range",
    "distance",
    "radius",
    "arc_range_degrees",
    "duration",
    # lifetime vs lifespan handled specially
}

# particle-related legacy mapping
PARTICLE_MAPPING = {
    "particle_count": "count",
    "particle_dispersion": "dispersion",
    "particle_colors": "colors",
    "particle_lifespan": "lifespan",
    "particle_speed": "speed",
    "size_range": "size_range",
    "color": "color",
    "emit_rate": "emit_rate",
}

IDENTITY_KEYS = {"id", "name", "type"}
SPRITE_KEYS = {"sprite", "scale"}


def _pop_many(src: Dict[str, Any], keys) -> Dict[str, Any]:
    out = {}
    for k in keys:
        if k in src:
            out[k] = src.pop(k)
    return out


def transform_spell(legacy: Dict[str, Any]) -> Dict[str, Any]:
    data = dict(legacy)  # shallow copy to consume
    new: Dict[str, Any] = {}

    # Identity
    for k in IDENTITY_KEYS:
        if k in data:
            new[k] = data.pop(k)

    # timings
    timings: Dict[str, Any] = {}
    for lk, nk in LEGACY_TO_TIMINGS.items():
        if lk in data:
            timings[nk] = data.pop(lk)
    if timings:
        new["timings"] = timings

    # rules
    rules = _pop_many(data, RULE_KEYS)
    if rules:
        new["rules"] = rules

    # constraints
    constraints = _pop_many(data, CONSTRAINT_KEYS)
    if constraints:
        new["constraints"] = constraints

    # effect
    effect = _pop_many(data, EFFECT_KEYS)
    # lifetime/lifespan
    lifetime = None
    if "lifetime" in data:
        lifetime = data.pop("lifetime")
    elif "lifespan" in data:
        lifetime = data.pop("lifespan")
    if lifetime is not None:
        effect["lifetime"] = lifetime
    # buff
    if "buff" in data:
        effect["buff"] = data.pop("buff")
    if effect:
        new["effect"] = effect

    # vfx
    vfx_obj = None
    vfx_value = data.pop("vfx", None)
    if isinstance(vfx_value, dict):
        vfx_obj = vfx_value
    else:
        vfx_obj = {}
        if isinstance(vfx_value, str):
            vfx_obj["preset"] = vfx_value
    # sprite
    sprite = {}
    sprite_fields = _pop_many(data, SPRITE_KEYS)
    if sprite_fields:
        if sprite_fields.get("sprite") is not None:
            sprite["path"] = sprite_fields.get("sprite")
        if "scale" in sprite_fields:
            sprite["scale"] = sprite_fields["scale"]
    # particles
    particles = {}
    for old, new_key in PARTICLE_MAPPING.items():
        if old in data:
            particles[new_key] = data.pop(old)
    # normalize colors list type
    if "colors" in particles and isinstance(particles["colors"], tuple):
        particles["colors"] = list(particles["colors"])  # type: ignore
    if sprite:
        vfx_obj["sprite"] = sprite
    if particles:
        vfx_obj["particles"] = particles
    if vfx_obj:
        new["vfx"] = vfx_obj

    # meta: anything left over that we didn't explicitly map
    leftovers = data  # whatever remains after pops
    if leftovers:
        new["meta"] = leftovers

    return new


def migrate(input_path: Path, output_path: Path, inplace: bool = False, backup: bool = False) -> Tuple[int, int]:
    with open(input_path, "r", encoding="utf-8") as f:
        raw: Dict[str, Dict[str, Any]] = json.load(f)
    migrated: Dict[str, Dict[str, Any]] = {}
    for key, entry in raw.items():
        migrated[key] = transform_spell(entry or {})
    if inplace:
        if backup:
            bak = input_path.with_suffix(input_path.suffix + ".bak")
            shutil.copy2(input_path, bak)
            print(f"Backup written: {bak}")
        with open(input_path, "w", encoding="utf-8") as f:
            json.dump(migrated, f, indent=2, ensure_ascii=False)
        print(f"Migrated in place: {input_path}")
    else:
        output_path.parent.mkdir(parents=True, exist_ok=True)
        with open(output_path, "w", encoding="utf-8") as f:
            json.dump(migrated, f, indent=2, ensure_ascii=False)
        print(f"Migrated written to: {output_path}")
    return (len(raw), len(migrated))


def main():
    ap = argparse.ArgumentParser(description="Migrate spells.json to new nested format")
    ap.add_argument("--input", type=Path, default=DEFAULT_INPUT, help="Input spells.json path")
    ap.add_argument("--output", type=Path, default=DEFAULT_OUTPUT, help="Output JSON path (ignored if --inplace)")
    ap.add_argument("--inplace", action="store_true", help="Overwrite input file in place")
    ap.add_argument("--backup", action="store_true", help="Create a .bak backup when using --inplace")
    args = ap.parse_args()

    if args.inplace:
        migrate(args.input, args.input, inplace=True, backup=args.backup)
    else:
        migrate(args.input, args.output, inplace=False)


if __name__ == "__main__":
    main()
