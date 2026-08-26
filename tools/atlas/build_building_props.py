#!/usr/bin/env python3
"""Stage sliced prop crops into the Unity project as building sprites.

Second half of the prop-sheet pipeline:

    slice_prop_sheet.py   sheet PNG            -> crops + slice manifest
    <classification>      crops                -> building_props_metadata.json
    build_building_props  crops + metadata     -> Resources/Buildings/<category>/*.png
                                                  + building_props_manifest.json
    BuildingPropImporter  manifest             -> BuildingTemplateData assets + catalog

What this step is actually for is scale. The crops come off a 1536x1024 sheet at
100-500 px tall; buildings render at 32 px per tile and the player is 2 tiles, so a
430 px street lamp would stand 13 tiles high. Every sprite is therefore resampled to
``target_height_tiles * 32`` pixels, in PREMULTIPLIED alpha (PIL's 'RGBa'), because
downscaling straight RGBA blends the zeroed RGB of transparent pixels into the edges
and rings every sprite with a dark halo.

Usage
-----
    python build_building_props.py --crops-root <dir> --metadata <json> [--dry-run]
"""

from __future__ import annotations

import argparse
import json
import os
import sys
from collections import Counter, defaultdict

import numpy as np
from PIL import Image

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
RESOURCES_BUILDINGS = os.path.join(
    REPO_ROOT, "unity", "Valkur", "Assets", "_Project", "Resources", "Buildings")
DEFAULT_MANIFEST = os.path.join(REPO_ROOT, "tools", "atlas", "generated",
                                "building_props_manifest.json")

# Buildings render at 32 px per world unit, and one world unit is one tile.
BUILDING_PPU = 32
# Categories this pipeline is allowed to write into. Keeping the list closed stops a
# typo in the metadata from creating a stray Resources folder that ships in the build.
CATEGORIES = ("lights", "signs", "market", "props", "nature",
              "military", "graveyard", "arcane", "blacksmith", "domestic",
              "bandit", "water", "statues", "quest", "houses", "shops")
# Keys LightPresetCatalog.asset actually defines. A fixture naming anything else
# would import silently and then light nothing at runtime.
LIGHT_PRESETS = ("Lamp", "Torch", "Magic", "Candle")
# Never let a resample produce something too small to read or big enough to blow up
# the buildings atlas.
MIN_EDGE_PX = 8
MAX_EDGE_PX = 1024

NAME_ALLOWED = set("abcdefghijklmnopqrstuvwxyz0123456789_")


def _repo_relative(path: str) -> str:
    """Repo-relative path, or the absolute one when it lives on another drive."""
    try:
        return os.path.relpath(path, REPO_ROOT).replace("\\", "/")
    except ValueError:
        return os.path.abspath(path).replace("\\", "/")


def validate(items: list[dict]) -> list[str]:
    """Every reason this metadata must not be written, collected in one pass."""
    errors: list[str] = []
    per_category: dict[str, Counter] = defaultdict(Counter)

    for it in items:
        tag = f"{it.get('sheet')}#{it.get('index')}"
        name = it.get("name", "")
        category = it.get("category", "")

        if not name:
            errors.append(f"{tag}: empty name")
        elif set(name) - NAME_ALLOWED:
            bad = "".join(sorted(set(name) - NAME_ALLOWED))
            errors.append(f"{tag}: name '{name}' has illegal characters '{bad}'")

        if category not in CATEGORIES:
            errors.append(f"{tag}: category '{category}' is not one of {CATEGORIES}")
        else:
            per_category[category][name] += 1

        split = it.get("split_ratio")
        if not isinstance(split, (int, float)) or not 0.0 <= split <= 1.0:
            errors.append(f"{tag}: split_ratio {split!r} outside [0,1]")

        tiles = it.get("target_height_tiles")
        if not isinstance(tiles, (int, float)) or not 0.2 <= tiles <= 40.0:
            errors.append(f"{tag}: target_height_tiles {tiles!r} outside [0.2,40]")

        if not isinstance(it.get("solid"), bool):
            errors.append(f"{tag}: solid must be a boolean")

        preset = it.get("light_preset")
        if preset is not None:
            if preset not in LIGHT_PRESETS:
                errors.append(f"{tag}: light_preset {preset!r} is not one of {LIGHT_PRESETS}")
            offset = it.get("light_offset_y", 0.75)
            if not isinstance(offset, (int, float)) or not 0.0 <= offset <= 1.0:
                errors.append(f"{tag}: light_offset_y {offset!r} outside [0,1]")

    for category, counts in per_category.items():
        for name, n in counts.items():
            if n > 1:
                errors.append(f"{category}/{name}: {n} items share this name")

    return errors


def resample(img: Image.Image, target_h: int) -> Image.Image:
    """Resize to ``target_h`` px tall, preserving aspect, without edge halos."""
    scale = target_h / img.height
    w = max(MIN_EDGE_PX, min(MAX_EDGE_PX, round(img.width * scale)))
    h = max(MIN_EDGE_PX, min(MAX_EDGE_PX, target_h))
    # 'RGBa' is PIL's premultiplied-alpha mode: resampling there keeps colour out of
    # the transparent pixels instead of averaging their zeroed RGB into every edge.
    out = img.convert("RGBa").resize((w, h), Image.LANCZOS).convert("RGBA")

    # LANCZOS overshoot leaves a 1-2 alpha fringe; below this it is invisible art but
    # visible atlas padding, so flatten it.
    arr = np.array(out)
    dead = arr[..., 3] < 6
    arr[dead] = 0
    return Image.fromarray(arr, "RGBA")


def build(crops_root: str, metadata_path: str, manifest_path: str, dry_run: bool) -> int:
    with open(metadata_path, encoding="utf-8") as fh:
        metadata = json.load(fh)
    items = metadata["items"]

    errors = validate(items)
    if errors:
        for e in errors:
            print(f"ERROR {e}", file=sys.stderr)
        print(f"\n{len(errors)} problem(s) in {metadata_path} - nothing was written.", file=sys.stderr)
        return 1

    entries = []
    written = 0
    for it in sorted(items, key=lambda i: (i["category"], i["name"])):
        src = os.path.join(crops_root, it["sheet"], f"{it['sheet']}_{it['index']:03d}.png")
        if not os.path.exists(src):
            print(f"ERROR missing crop {src}", file=sys.stderr)
            return 1

        img = Image.open(src).convert("RGBA")
        target_h = max(MIN_EDGE_PX, round(it["target_height_tiles"] * BUILDING_PPU))
        out = resample(img, target_h)

        rel_dir = it["category"]
        dst_dir = os.path.join(RESOURCES_BUILDINGS, rel_dir)
        dst = os.path.join(dst_dir, f"{it['name']}.png")

        if not dry_run:
            os.makedirs(dst_dir, exist_ok=True)
            out.save(dst)
            written += 1

        entries.append({
            "name": it["name"],
            "category": rel_dir,
            "resourcePath": f"Buildings/{rel_dir}/{it['name']}",
            "sourceImagePath": f"assets/buildings/{rel_dir}/{it['name']}.png",
            "solid": bool(it["solid"]),
            "splitRatio": round(float(it["split_ratio"]), 4),
            "colliderScope": "CG",
            "width": out.width,
            "height": out.height,
            "sheet": it["sheet"],
            "sheetIndex": int(it["index"]),
            # Empty key = the template keeps whatever it had; the importer only
            # writes the light fields when a key is actually present.
            "lightPresetKey": it.get("light_preset", ""),
            "lightOffsetY": round(float(it.get("light_offset_y", 0.75)), 3),
        })

        print(f"{'(dry) ' if dry_run else ''}{rel_dir}/{it['name']}.png  "
              f"{img.width}x{img.height} -> {out.width}x{out.height}  "
              f"({it['target_height_tiles']}t, split {it['split_ratio']}, "
              f"solid {str(it['solid']).lower()})")

    manifest = {
        "generator": "tools/atlas/build_building_props.py",
        "generatedFrom": _repo_relative(metadata_path),
        "entries": entries,
    }
    if not dry_run:
        os.makedirs(os.path.dirname(manifest_path), exist_ok=True)
        with open(manifest_path, "w", encoding="utf-8") as fh:
            json.dump(manifest, fh, indent=2)

    by_cat = Counter(e["category"] for e in entries)
    print(f"\n{len(entries)} sprites " + ("planned" if dry_run else f"written ({written} PNGs)"))
    for cat, n in sorted(by_cat.items()):
        print(f"  {cat:8s} {n}")
    if not dry_run:
        print(f"manifest -> {manifest_path}")
    return 0


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--crops-root", required=True,
                    help="Directory holding one subfolder of crops per sheet")
    ap.add_argument("--metadata", required=True,
                    help="JSON with the per-sprite name/category/size classification")
    ap.add_argument("--manifest", default=DEFAULT_MANIFEST,
                    help="Where to write the manifest the Unity importer reads")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()
    return build(args.crops_root, args.metadata, args.manifest, args.dry_run)


if __name__ == "__main__":
    raise SystemExit(main())
