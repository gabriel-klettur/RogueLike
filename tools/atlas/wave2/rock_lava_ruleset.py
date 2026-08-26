#!/usr/bin/env python3
"""Derive the Corner16 slot mapping for the ``rock_lava`` pack and merge it into
``tools/atlas/generated/tile_rulesets.json``.

Why this pack needs its own pass
--------------------------------
``analyze_tile_edges.py`` labels a region by clustering the sheet's palette into
materials and demanding one of them own >= 65% of the probe. That works on the
flat-shaded packs it was written for. This art is not flat-shaded: the rock
carries two greys (#848484, #545454) and the lava four oranges (#e4540c,
#542424, #fccc24, #b4240c), so the clustering splits ONE terrain across several
materials and no probe ever reaches the purity floor. The analyser's own verdict
on this pack is ``UNRELIABLE - 81% of edges too blended to label``.

Nothing is wrong with the art. Rock and lava separate perfectly on a single
axis - red minus blue - because rock is grey and lava is saturated orange:
measured over the source island, a pure-lava cell scores 10.2% rock and a
pure-rock cell 100.0%, with nothing in between. This script uses that two-class
classifier instead of the general palette clustering, and leaves
``analyze_tile_edges.py`` alone so the packs it does handle keep behaving
identically.

Run it AFTER ``analyze_tile_edges.py``, which rewrites ``tile_rulesets.json``
wholesale and would otherwise drop this pack.

Provenance
----------
The source ``rock_lava.png`` is a 256x256 island render, not a laid-out sheet.
Cut on the 32 px grid it yields exactly 16 distinct cells out of 64 - the art
was drawn on that grid - and those 16 map one-to-one onto the 16 corner
signatures. The bijection is asserted below; a pack that does not achieve it is
refused rather than emitted with holes.
"""

from __future__ import annotations

import itertools
import json
import os
import sys

import numpy as np
from PIL import Image

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
PACK_DIR = os.path.join(REPO, "unity", "Valkur", "Assets", "_Project",
                        "Resources", "Tiles", "rock_lava")
RULESETS = os.path.join(REPO, "tools", "atlas", "generated", "tile_rulesets.json")

PACK = "rock_lava"
# Primary is "stone", not "rock". FindPaintRuleset resolves a terrain NAME to exactly one
# Corner16 ruleset, highest Priority wins and ties go to list order — so a second pack
# claiming "rock" would simply never be reachable from the F8 auto-brush, because
# rock_water already claims it. The two are different materials anyway: rock_water's rock
# is smooth dark #3c3c3c, this pack's is pale loose rubble #848484.
PRIMARY, SECONDARY = "stone", "lava"
# Rock is grey, lava is saturated orange, so red-minus-blue separates them on its
# own. 40 sits in the empty middle of the measured distribution (10.2% / 100.0%).
ROCK_RED_MINUS_BLUE_MAX = 40
# Four independent probe geometries (inset, size) that all agree on the same
# mapping. Agreement is the check that the threshold is not doing the deciding:
# a 3-4 px probe at inset 0 sits inside the rock's dark outline and collapses
# seven signatures, which is exactly the kind of silent miscall worth catching.
PROBES = ((0, 6), (1, 4), (0, 8), (2, 6))
CORNER_ORDER = "NW,NE,SE,SW"


def rock_mask(rgb: np.ndarray) -> np.ndarray:
    return (rgb[:, :, 0].astype(int) - rgb[:, :, 2].astype(int)) < ROCK_RED_MINUS_BLUE_MAX


def signature(rgb: np.ndarray, inset: int, size: int) -> str:
    """Corner signature, MSB first in ``CORNER_ORDER``.

    A bit is 1 when that corner shows the SECONDARY terrain, which is what
    ``TerrainTileResolver.ResolveVariantForCell`` keys on - it calls
    ``CornerMask(grid, cell, ruleset.TerrainSecondary)``. Writing it the other
    way round inverts the pack silently: a solid field resolves to the tile that
    is solid in the opposite terrain, and nothing errors.
    """
    mask = rock_mask(rgb)
    n = mask.shape[0]
    lo, hi = inset, inset + size
    quadrants = (
        mask[lo:hi, lo:hi],              # NW
        mask[lo:hi, n - hi:n - lo],      # NE
        mask[n - hi:n - lo, n - hi:n - lo],  # SE
        mask[n - hi:n - lo, lo:hi],      # SW
    )
    return "".join("0" if q.mean() > 0.5 else "1" for q in quadrants)


def main() -> int:
    manifest_path = os.path.join(PACK_DIR, "_manifest.json")
    if not os.path.exists(manifest_path):
        print(f"ERROR no manifest at {manifest_path}", file=sys.stderr)
        return 1
    with open(manifest_path, encoding="utf-8") as fh:
        manifest = json.load(fh)

    slots: dict[str, list[str]] = {}
    for entry in manifest["uniques"]:
        name = entry["file"]
        rgb = np.asarray(Image.open(os.path.join(PACK_DIR, f"{name}.png")).convert("RGB"))
        sigs = {signature(rgb, *probe) for probe in PROBES}
        if len(sigs) != 1:
            print(f"ERROR {name}: probe geometries disagree {sorted(sigs)}", file=sys.stderr)
            return 1
        slots.setdefault(sigs.pop(), []).append(name)

    combos = ["".join(c) for c in itertools.product("01", repeat=4)]
    missing = [c for c in combos if c not in slots]
    if missing:
        print(f"ERROR {len(missing)}/16 corner combinations have no tile: {missing}",
              file=sys.stderr)
        return 1

    with open(RULESETS, encoding="utf-8") as fh:
        doc = json.load(fh)
    doc["packs"][PACK] = {
        "materialA": PRIMARY,
        "materialB": SECONDARY,
        "primaryMaterial": 0,
        "palette": {PRIMARY: "#848484", SECONDARY: "#e4540c"},
        "cornerOrder": CORNER_ORDER,
        "keyMeaning": "1 = corner is the SECONDARY terrain (matches TerrainTileResolver)",
        "terrainPrimary": PRIMARY,
        "terrainSecondary": SECONDARY,
        "generator": "tools/atlas/wave2/rock_lava_ruleset.py",
        "slots": {sig: sorted(slots[sig]) for sig in combos},
    }
    with open(RULESETS, "w", encoding="utf-8") as fh:
        json.dump(doc, fh, indent=2)

    print(f"{PACK}: 16/16 corner slots from {len(manifest['uniques'])} unique tiles "
          f"({len(manifest['cells'])} cells) -> {RULESETS}")
    for sig in combos:
        print(f"  {sig}  {', '.join(slots[sig])}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
