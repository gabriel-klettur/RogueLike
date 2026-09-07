#!/usr/bin/env python3
"""Cut the 2026-09-06 grass/dirt wave into picker categories and merge it into the
``grass_dirt`` Corner16 ruleset.

The wave
--------
Five 256x256 island renders under ``Art/Tiles/_source/new/``. Each cuts on the 32 px
grid into exactly 16 distinct cells out of 64 — the art was drawn on that grid — and
those 16 map one-to-one onto the 16 corner signatures. The bijection is asserted per
sheet; a sheet that does not achieve it is refused rather than emitted with holes.

  grass_dirt2 .. grass_dirt6   five VARIANTS of the grass/dirt pair

One CATEGORY per sheet, one RULESET for all of them
---------------------------------------------------
Each sheet becomes its own folder under ``Resources/Tiles/``, so the Tile editor shows
it as its own tab and a stroke draws from one coherent sheet — a pack merged from
several sheets otherwise scatters all of them across a single stroke, which was
measured on ``grass_rock`` as seven visually different arts cell by cell.

They all feed the ONE ``grass_dirt`` ruleset, because ``FindPaintRuleset`` resolves a
terrain NAME to exactly one ruleset: five packs claiming ``grass`` would leave four of
them permanently unreachable, silently. The ruleset's ``sheetFolders`` array is what
lets ``TilesetRulesetImporter`` find sprites that live outside the pack's own folder.

Why the classifier is a single measured axis
--------------------------------------------
``analyze_tile_edges.py`` clusters the palette into materials and demands one own >=65%
of a probe; these sheets are anti-aliased and that splits one terrain across entries.
Grass and dirt separate cleanly on GREEN MINUS RED instead, measured across all five
sheets: dirt never exceeds -11 and grass never falls below +22, so a threshold of 20
sits inside a gap of 34 to 72 with not one pixel in between.

Run it AFTER ``analyze_tile_edges.py``, which rewrites ``tile_rulesets.json`` wholesale
and would otherwise drop what this adds.
"""

from __future__ import annotations

import hashlib
import itertools
import json
import os
import sys

import numpy as np
from PIL import Image

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
SOURCE = os.path.join(REPO, "unity", "Valkur", "Assets", "_Project", "Art", "Tiles", "_source", "new")
TILES = os.path.join(REPO, "unity", "Valkur", "Assets", "_Project", "Resources", "Tiles")
RULESETS = os.path.join(REPO, "tools", "atlas", "generated", "tile_rulesets.json")

PACK = "grass_dirt"
SHEETS = ["grass_dirt2", "grass_dirt3", "grass_dirt4", "grass_dirt5", "grass_dirt6"]

CELL = 32
GRID = 8
CORNER_ORDER = "NW,NE,SE,SW"
KEY_MEANING = "1 = corner is the SECONDARY terrain (matches TerrainTileResolver)"

# Four independent probe geometries (inset, size) that must all agree. Agreement is what
# proves the threshold is not doing the deciding: a probe sitting inside a tile's dark
# outline collapses several signatures, which is exactly the kind of silent miscall worth
# catching.
PROBES = ((0, 6), (1, 4), (0, 8), (2, 6))

GRASS_MINUS_RED = 20


def grass_mask(rgb: np.ndarray) -> np.ndarray:
    """True where the pixel is GRASS, the pack's PRIMARY terrain."""
    return (rgb[:, :, 1].astype(int) - rgb[:, :, 0].astype(int)) > GRASS_MINUS_RED


def signature(rgb: np.ndarray, inset: int, size: int) -> str:
    """Corner signature, MSB first in CORNER_ORDER.

    A bit is 1 when that corner shows the SECONDARY terrain, which is what
    TerrainTileResolver.ResolveVariantForCell keys on — it calls
    CornerMask(grid, cell, ruleset.TerrainSecondary). Writing it the other way round
    inverts the pack silently: a solid grass field resolves to the all-dirt tile, and
    nothing errors.
    """
    mask = grass_mask(rgb)
    n = mask.shape[0]
    lo, hi = inset, inset + size
    quadrants = (
        mask[lo:hi, lo:hi],                  # NW
        mask[lo:hi, n - hi:n - lo],          # NE
        mask[n - hi:n - lo, n - hi:n - lo],  # SE
        mask[n - hi:n - lo, lo:hi],          # SW
    )
    return "".join("0" if q.mean() > 0.5 else "1" for q in quadrants)


def slice_sheet(name: str) -> tuple[dict[str, list[str]], dict] | None:
    """Cut one sheet to disk. Returns (signature -> sprite names, manifest) or None."""
    src = os.path.join(SOURCE, f"{name}.png")
    if not os.path.exists(src):
        print(f"ERROR {name}: no source at {src}", file=sys.stderr)
        return None

    image = np.asarray(Image.open(src).convert("RGB"))
    if image.shape[:2] != (CELL * GRID, CELL * GRID):
        print(f"ERROR {name}: expected {CELL*GRID}x{CELL*GRID}, got {image.shape[1]}x{image.shape[0]}",
              file=sys.stderr)
        return None

    out_dir = os.path.join(TILES, name)
    os.makedirs(out_dir, exist_ok=True)

    slots: dict[str, list[str]] = {}
    cells, uniques, by_hash = [], [], {}

    for r in range(GRID):
        for c in range(GRID):
            block = image[r * CELL:(r + 1) * CELL, c * CELL:(c + 1) * CELL]
            cell_name = f"{name}_r{r:02d}_c{c:02d}"
            Image.fromarray(block).save(os.path.join(out_dir, f"{cell_name}.png"))

            digest = hashlib.sha256(block.tobytes()).hexdigest()
            if digest not in by_hash:
                by_hash[digest] = len(uniques)
                uniques.append({"id": len(uniques), "file": cell_name, "hash": digest})

                sigs = {signature(block, *probe) for probe in PROBES}
                if len(sigs) != 1:
                    print(f"ERROR {cell_name}: probe geometries disagree {sorted(sigs)}", file=sys.stderr)
                    return None
                slots.setdefault(sigs.pop(), []).append(cell_name)

            cells.append({"r": r, "c": c, "file": cell_name,
                          "uniqueId": by_hash[digest], "transparent": False})

    combos = ["".join(x) for x in itertools.product("01", repeat=4)]
    missing = [s for s in combos if s not in slots]
    if missing:
        print(f"ERROR {name}: {len(missing)}/16 corner combinations have no tile: {missing}",
              file=sys.stderr)
        return None
    extra = {s: v for s, v in slots.items() if len(v) > 1}
    if extra:
        print(f"ERROR {name}: signatures claimed by more than one tile: {extra}", file=sys.stderr)
        return None

    manifest = {"schemaVersion": 1, "source": name, "cellPx": CELL,
                "cols": GRID, "rows": GRID, "cells": cells, "uniques": uniques}
    with open(os.path.join(out_dir, "_manifest.json"), "w", encoding="utf-8", newline="\n") as fh:
        json.dump(manifest, fh, indent=1)

    print(f"{name}: {GRID*GRID} cells, {len(uniques)} unique, 16/16 corner slots")
    return slots, manifest


def main() -> int:
    with open(RULESETS, encoding="utf-8") as fh:
        doc = json.load(fh)

    entry = doc["packs"].get(PACK)
    if entry is None:
        print(f"ERROR {PACK}: no such pack in {RULESETS}", file=sys.stderr)
        return 1

    merged: dict[str, list[str]] = {}
    for name in SHEETS:
        result = slice_sheet(name)
        if result is None:
            return 1
        for sig, names in result[0].items():
            merged.setdefault(sig, []).extend(names)

    combos = ["".join(x) for x in itertools.product("01", repeat=4)]
    existing = entry.setdefault("slots", {})
    for sig in combos:
        have = list(existing.get(sig, []))
        for name in merged.get(sig, []):
            if name not in have:
                have.append(name)
        existing[sig] = sorted(have)

    # Where the importer must look for this pack's sprites. It scopes the search to the
    # pack's own folder — deliberately, so a sprite name that exists in two packs cannot be
    # taken from the wrong one — and a sheet that is its own picker category lives at the
    # Tiles root instead. Only folders outside the pack are listed.
    declared = sorted(set(entry.get("sheetFolders", [])) | set(SHEETS))
    entry["sheetFolders"] = declared
    entry["generator"] = "tools/atlas/wave9/grass_dirt_sheets.py"

    counts = {sig: len(existing[sig]) for sig in combos}
    print(f"\n{PACK}: variants per slot now {min(counts.values())}-{max(counts.values())} "
          f"({sum(counts.values())} sprites over 16 slots)")
    print(f"{PACK}.sheetFolders = {declared}")

    with open(RULESETS, "w", encoding="utf-8", newline="\n") as fh:
        json.dump(doc, fh, indent=2)
    print(f"wrote {RULESETS}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
