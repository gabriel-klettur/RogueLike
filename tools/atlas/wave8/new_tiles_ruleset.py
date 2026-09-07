#!/usr/bin/env python3
"""Derive the Corner16 slot mapping for the 2026-09-06 tile wave and merge it into
``tools/atlas/generated/tile_rulesets.json``.

The wave
--------
Five 256x256 island renders, cut on the 32 px grid. Each yields exactly 16
distinct cells out of 64 — the art was drawn on that grid — and those 16 map
one-to-one onto the 16 corner signatures. The bijection is asserted per sheet;
a sheet that does not achieve it is refused rather than emitted with holes.

  grass_rock_1..4   four VARIANTS of the grass/rock pair
  water_water_deep  a new pair, water over deep water

Why the four grass_rock sheets are four CATEGORIES but one PACK
---------------------------------------------------------------
Each sheet is its own tab in the Tile editor's picker — a category is a folder
under ``Resources/Tiles/``, and one tileset per tab is what an author browses
by. That is a presentation split and it costs the auto-brush nothing. Turning
them into four RULESETS would, and here is why:

``TerrainCatalog.FindPaintRuleset`` resolves a terrain NAME to exactly ONE
Corner16 ruleset (highest Priority, ties by list order), so four packs all
claiming ``grass`` would leave three of them permanently unreachable from the
auto-brush, silently. Variants belong INSIDE a ruleset instead: a slot holds a
LIST of sprites and ``RulesetSolver.ResolveVariant`` picks one deterministically
from the cell hash, so the same cell always renders the same tile while the
field as a whole stops repeating. The four sheets are genuinely different art —
measured, only 1 of 16 cells is shared between any pair, the flat grass one —
and their palettes match the pack already in the catalog (grass #528539 vs
#54843c, rock #837d73 vs #84846c), so mixing them reads as one terrain.

Why the classifier is per-pair and not the general analyser
-----------------------------------------------------------
``analyze_tile_edges.py`` clusters the sheet palette into materials and demands
one own >= 65% of a probe. These sheets are anti-aliased, so as with
``rock_lava`` the clustering splits one terrain across several entries. Each
pair here separates cleanly on a single measured axis instead:

  grass vs rock        green minus red   grass +51, rock -6      (threshold 20)
  water vs water_deep  luminance         shallow 123, deep 66    (threshold 95)

Run it AFTER ``analyze_tile_edges.py``, which rewrites ``tile_rulesets.json``
wholesale and would otherwise drop what this adds.
"""

from __future__ import annotations

import itertools
import json
import os
import sys

import numpy as np
from PIL import Image

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
TILES = os.path.join(REPO, "unity", "Valkur", "Assets", "_Project", "Resources", "Tiles")
RULESETS = os.path.join(REPO, "tools", "atlas", "generated", "tile_rulesets.json")

CORNER_ORDER = "NW,NE,SE,SW"
KEY_MEANING = "1 = corner is the SECONDARY terrain (matches TerrainTileResolver)"

# Four independent probe geometries (inset, size) that must all agree. Agreement is
# what proves the threshold is not doing the deciding: a probe sitting inside a
# tile's dark outline collapses several signatures, which is exactly the kind of
# silent miscall worth catching.
PROBES = ((0, 6), (1, 4), (0, 8), (2, 6))


def grass_mask(rgb: np.ndarray) -> np.ndarray:
    """True where the pixel is GRASS. Green minus red: grass +51, rock -6."""
    return (rgb[:, :, 1].astype(int) - rgb[:, :, 0].astype(int)) > 20


def shallow_mask(rgb: np.ndarray) -> np.ndarray:
    """True where the pixel is SHALLOW water. Luminance: shallow 123, deep 66."""
    lum = (0.299 * rgb[:, :, 0] + 0.587 * rgb[:, :, 1] + 0.114 * rgb[:, :, 2])
    return lum > 95


SHEETS = [
    # (ruleset pack, sheet folder under Resources/Tiles/, sheet prefix, primary-terrain mask)
    #
    # The pack and the folder are separate on purpose. Each sheet is its OWN picker
    # category (one tab per tileset), so it lives at the Tiles root and carries its own
    # _manifest.json; the RULESET they all feed is still the single `grass_rock` one,
    # for the reason in the module docstring.
    ("grass_rock",       "grass_rock_1",     "grass_rock_1", grass_mask),
    ("grass_rock",       "grass_rock_2",     "grass_rock_2", grass_mask),
    ("grass_rock",       "grass_rock_3",     "grass_rock_3", grass_mask),
    ("grass_rock",       "grass_rock_4",     "grass_rock_4", grass_mask),
    ("water_water_deep", "water_water_deep", "water_deep",   shallow_mask),
]

NEW_PACKS = {
    "water_water_deep": {
        "materialA": "water",
        "materialB": "water_deep",
        "primaryMaterial": 0,
        "palette": {"water": "#3490ce", "water_deep": "#08538b"},
        "terrainPrimary": "water",
        "terrainSecondary": "water_deep",
    },
}


def signature(rgb: np.ndarray, mask_fn, inset: int, size: int) -> str:
    """Corner signature, MSB first in CORNER_ORDER.

    A bit is 1 when that corner shows the SECONDARY terrain, which is what
    TerrainTileResolver.ResolveVariantForCell keys on — it calls
    CornerMask(grid, cell, ruleset.TerrainSecondary). Writing it the other way
    round inverts the pack silently: a solid field resolves to the tile that is
    solid in the opposite terrain, and nothing errors.
    """
    mask = mask_fn(rgb)
    n = mask.shape[0]
    lo, hi = inset, inset + size
    quadrants = (
        mask[lo:hi, lo:hi],                  # NW
        mask[lo:hi, n - hi:n - lo],          # NE
        mask[n - hi:n - lo, n - hi:n - lo],  # SE
        mask[n - hi:n - lo, lo:hi],          # SW
    )
    return "".join("0" if q.mean() > 0.5 else "1" for q in quadrants)


def slots_for_sheet(folder: str, prefix: str, mask_fn) -> dict[str, list[str]] | None:
    """Signature -> sprite names for one sheet, or None when it is not a clean Corner16."""
    sheet_dir = os.path.join(TILES, folder)
    manifest_path = os.path.join(sheet_dir, "_manifest.json")
    if not os.path.exists(manifest_path):
        print(f"ERROR {prefix}: no manifest at {manifest_path}", file=sys.stderr)
        return None
    with open(manifest_path, encoding="utf-8") as fh:
        manifest = json.load(fh)

    uniques = [e for e in manifest["uniques"] if e["file"].startswith(prefix)]
    slots: dict[str, list[str]] = {}
    for entry in uniques:
        name = entry["file"]
        png = os.path.join(sheet_dir, f"{name}.png")
        rgb = np.asarray(Image.open(png).convert("RGB"))
        sigs = {signature(rgb, mask_fn, *probe) for probe in PROBES}
        if len(sigs) != 1:
            print(f"ERROR {name}: probe geometries disagree {sorted(sigs)}", file=sys.stderr)
            return None
        slots.setdefault(sigs.pop(), []).append(name)

    combos = ["".join(c) for c in itertools.product("01", repeat=4)]
    missing = [c for c in combos if c not in slots]
    if missing:
        print(f"ERROR {prefix}: {len(missing)}/16 corner combinations have no tile: {missing}",
              file=sys.stderr)
        return None
    extra = {s: v for s, v in slots.items() if len(v) > 1}
    if extra:
        print(f"ERROR {prefix}: signatures claimed by more than one tile: {extra}", file=sys.stderr)
        return None
    return slots


def main() -> int:
    with open(RULESETS, encoding="utf-8") as fh:
        doc = json.load(fh)
    combos = ["".join(c) for c in itertools.product("01", repeat=4)]

    per_pack: dict[str, dict[str, list[str]]] = {}
    for pack, folder, prefix, mask_fn in SHEETS:
        slots = slots_for_sheet(folder, prefix, mask_fn)
        if slots is None:
            return 1
        print(f"{prefix}: 16/16 corner slots")
        merged = per_pack.setdefault(pack, {})
        for sig, names in slots.items():
            merged.setdefault(sig, []).extend(names)

    for pack, slots in per_pack.items():
        entry = doc["packs"].get(pack)
        if entry is None:
            spec = NEW_PACKS.get(pack)
            if spec is None:
                print(f"ERROR {pack}: new pack with no declaration in NEW_PACKS", file=sys.stderr)
                return 1
            entry = dict(spec)
            entry["cornerOrder"] = CORNER_ORDER
            entry["keyMeaning"] = KEY_MEANING
            entry["slots"] = {sig: [] for sig in combos}
            entry["extraSignatures"] = {}
            doc["packs"][pack] = entry
            print(f"{pack}: new pack, primary='{entry['terrainPrimary']}' "
                  f"secondary='{entry['terrainSecondary']}'")

        entry["generator"] = "tools/atlas/wave8/new_tiles_ruleset.py"

        # Where TilesetRulesetImporter must look for this pack's sprites. It scopes the
        # search to the pack's own folder — deliberately, so a sprite name that exists in
        # two packs cannot be taken from the wrong one — and a sheet that is its own picker
        # category lives at the Tiles root instead. Declaring the folders keeps the scoping
        # while letting the category split and the pack split differ. Only folders that
        # really sit outside the pack are listed; a pack whose sheets are still nested needs
        # no entry and gets the old behaviour exactly.
        outside = sorted({
            folder for pack_name, folder, _, _ in SHEETS
            if pack_name == pack and folder != pack
        })
        if outside:
            entry["sheetFolders"] = outside
        existing = entry.setdefault("slots", {})
        for sig in combos:
            have = list(existing.get(sig, []))
            for name in slots.get(sig, []):
                if name not in have:
                    have.append(name)
            existing[sig] = sorted(have)

        counts = {sig: len(existing[sig]) for sig in combos}
        print(f"{pack}: variants per slot now {min(counts.values())}-{max(counts.values())} "
              f"({sum(counts.values())} sprites over 16 slots)")

    with open(RULESETS, "w", encoding="utf-8") as fh:
        json.dump(doc, fh, indent=2)
    print(f"\nwrote {RULESETS}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
