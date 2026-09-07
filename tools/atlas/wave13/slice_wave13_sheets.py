#!/usr/bin/env python3
"""Key the wave13 monster sheets and emit the ``*.slices.json`` the frame builder reads.

WHERE THIS SITS
---------------
::

    slice_wave13_sheets.py   sheet PNG  -> keyed RGBA + <sheet>.slices.json
    build_monster_frames.py  slices      -> Art/NPC/monsters/<key>/*.png + manifest
    MonsterFramesImporter    manifest    -> MonsterDefinition assets + MonsterCatalog

It replaces ``slice_prop_sheet.py`` for this wave and writes the SAME interface, so
alignment, scaling, mirroring and the manifest all stay owned by
``build_monster_frames.py``. There is exactly one implementation of "put an animation on
a shared canvas with its ground line pinned", and this wave does not fork it.

WHY NOT slice_prop_sheet.py
---------------------------
**The sheets have no alpha.** ``barbol_muscle`` and ``barbol_young`` ship as RGB PNGs
with the editor's transparency CHECKERBOARD baked into the pixels. Every segmenter in
this repo thresholds alpha, so those sheets segment as one object the size of the sheet
with a grey chequerboard welded to the character.

KEYING IS A BORDER FLOOD FILL, NOT A THRESHOLD
----------------------------------------------
The checkerboard is near-white and achromatic (measured 247-253 on all three channels)
and so are a barbol's teeth, the whites of its eyes and the specular on its belt bead. A
global "bright pixels are background" rule punches holes through exactly those, and the
holes are small enough to pass review and obvious in game. Background is by definition
the region reachable from the sheet border; an eye is not. Verified by compositing a
keyed frame over magenta and looking for bleed-through: none.

The antialiased rim gets PARTIAL alpha rather than a hard cut, because a hard cut leaves
a one-pixel white halo that survives downscaling as a bright fringe.

THE FRAMES ARE NOT ON A GRID, AND ASSUMING ONE IS WRONG
-------------------------------------------------------
This is the finding that shaped the whole file. The sheets LOOK like even grids and are
not: measured on ``barbol_muscle_idle``, the figures are about 376 px wide on a 362 px
nominal cell, so every figure overlaps its neighbours' cells and 148 components straddle
a cut line across the wave. Slicing on the grid puts a slice of the next barbol into
this barbol's frame.

Three ways of finding the frames were tried before this one:

* **Column-gap projection.** Reports 5 frames for a sheet that holds 6, because
  overlapping bounding boxes leave no empty column between them.
* **Periodicity scoring.** Prefers 2 almost everywhere: a layout with one cut line has
  one chance to score badly and a layout with seven has seven.
* **Reading the contact sheets by eye.** Got ``barbol_heavy_attack`` wrong (read 6, it
  is 8) and ``barbol_muscle_run`` wrong (read 6, it is 8) at thumbnail size.

What works is CONNECTED COMPONENTS, because the figures overlap in bounding box and do
not touch: there is always transparent space between two barbols even where their boxes
cross. Small satellites (a flung leaf, a detached fingertip) are attached to the nearest
core, which is the same core-seed clustering ``wave7/build_spell_icons.py`` uses.

The config still declares the expected count per sheet. Not as the source of truth --
the segmentation is -- but as a CHECK: a sheet that segments into a different number of
figures than a human counted is a sheet where something is wrong, and it should stop the
build rather than quietly ship a frame made of two barbols.

ANCHORS ARE A FITTED PITCH, NOT A BODY CENTRE
----------------------------------------------
``build_monster_frames.py`` anchors each frame on its CELL centre rather than its body,
because anchoring on the body cancels the motion the animation is made of -- a walk's
hip sway, a lunge's reach. With no real grid there is no cell centre, so this tool fits
one: an evenly spaced ladder through the measured figure centres, whose pitch is the
mean spacing. Measured across this wave the fit is good to about 5 %, and the residual
IS the motion -- a running barbol's body genuinely sits ahead of its slot.

Usage
-----
    python slice_wave13_sheets.py --config wave13.config.json --src <dir> --out <dir>
"""

from __future__ import annotations

import argparse
import json
import os
import sys
from typing import Dict, List, Tuple

import numpy as np
from PIL import Image
from scipy import ndimage

# The checkerboard measured on this wave: every channel at or above this, and near-grey.
BG_MIN_LEVEL = 200
BG_MAX_CHROMA = 22

# Luminance band across which the antialiased rim ramps from opaque to clear.
RIM_OPAQUE_AT = 200.0
RIM_CLEAR_AT = 250.0

# Below this a pixel is not part of a figure.
ALPHA_KEEP = 16

# A component smaller than this fraction of the biggest one is a satellite, not a figure.
CORE_MIN_FRACTION = 0.04


def key_out_checkerboard(im: Image.Image) -> Image.Image:
    """RGB with a baked checkerboard -> RGBA with the checkerboard removed."""
    rgb = np.asarray(im.convert("RGB")).astype(np.int16)
    h, w, _ = rgb.shape

    lo = rgb.min(axis=2)
    hi = rgb.max(axis=2)
    bright_grey = (lo >= BG_MIN_LEVEL) & ((hi - lo) <= BG_MAX_CHROMA)

    labels, count = ndimage.label(bright_grey)
    if count == 0:
        return Image.fromarray(
            np.dstack([rgb.astype(np.uint8), np.full((h, w), 255, np.uint8)]), "RGBA")

    border = set(labels[0, :].tolist()) | set(labels[-1, :].tolist())
    border |= set(labels[:, 0].tolist()) | set(labels[:, -1].tolist())
    border.discard(0)

    # ENCLOSED POCKETS COUNT AS BACKGROUND TOO, and missing them was the first version's
    # real bug. A gap between a barbol's arm and its torso is plate that does not touch the
    # sheet border, so a border-only fill leaves it opaque — and it comes through as a solid
    # white patch welded to the character's hip. Measured across this wave: 146 such pockets,
    # on 39 of the 41 RGB sheets.
    #
    # What separates a pocket from an eye white is not SIZE, which was tried and is a
    # fragile proxy (real pockets ran 90 to 2263 px and highlights up to 175). It is that a
    # plate is WIDE: it survives an erosion, and a tooth, an earring glint or the white of an
    # eye does not. Reconstructing the eroded seed back through the bright mask then recovers
    # each pocket's true edge rather than its eroded core.
    seed = ndimage.binary_erosion(bright_grey, structure=np.ones((7, 7)))
    plate_labels = set(np.unique(labels[seed]).tolist())
    plate_labels.discard(0)

    background = np.isin(labels, list(border | plate_labels))

    # The slivers the erosion cannot reach: a thread of plate between two fingers is too
    # narrow to survive a 7x7 seed and is not connected to the border either. Measured, they
    # are tiny (largest 83 px on a 1855 px sheet) and they land as white specks welded to a
    # hand. Cleared by the one property that separates plate from any highlight this art
    # actually contains: the plate is BOTH near-white AND colourless, while a tooth, an
    # earring glint and an eye white all carry a tint. Restricted to components that are
    # ENTIRELY that colour and small, so a genuinely white feature of any size survives.
    strict = (lo >= 240) & ((hi - lo) <= 6) & ~background
    strict_labels, strict_count = ndimage.label(strict)
    if strict_count:
        strict_sizes = np.bincount(strict_labels.ravel())
        strict_sizes[0] = 0
        slivers = [i for i in range(1, strict_count + 1) if 0 < strict_sizes[i] <= 400]
        if slivers:
            background = background | np.isin(strict_labels, slivers)

    luma = rgb.mean(axis=2)
    alpha = np.full((h, w), 255.0)
    ramp = np.clip((RIM_CLEAR_AT - luma) / (RIM_CLEAR_AT - RIM_OPAQUE_AT), 0.0, 1.0)
    near_bg = ndimage.binary_dilation(background, iterations=2) & ~background
    alpha[near_bg] = ramp[near_bg] * 255.0
    alpha[background] = 0.0

    return Image.fromarray(np.dstack([rgb.astype(np.uint8), alpha.astype(np.uint8)]), "RGBA")


def find_figures(alpha: np.ndarray) -> List[Tuple[int, int, int, int]]:
    """One box per figure, unordered. See the module docstring for why components."""
    solid = alpha >= ALPHA_KEEP
    labels, count = ndimage.label(solid, structure=np.ones((3, 3)))
    if count == 0:
        return []

    sizes = np.bincount(labels.ravel())
    sizes[0] = 0
    biggest = int(sizes.max())
    cores = [i for i in range(1, count + 1) if sizes[i] >= biggest * CORE_MIN_FRACTION]
    if not cores:
        return []

    centres = {i: ndimage.center_of_mass(labels == i)
               for i in range(1, count + 1) if sizes[i] > 0}
    boxes = ndimage.find_objects(labels)

    groups: Dict[int, List[int]] = {c: [c] for c in cores}
    for i in range(1, count + 1):
        if sizes[i] == 0 or i in cores:
            continue
        yi, xi = centres[i]
        nearest = min(cores, key=lambda c: (centres[c][1] - xi) ** 2 + (centres[c][0] - yi) ** 2)
        groups[nearest].append(i)

    out = []
    for members in groups.values():
        xs: List[int] = []
        ys: List[int] = []
        for m in members:
            sl = boxes[m - 1]
            ys += [sl[0].start, sl[0].stop]
            xs += [sl[1].start, sl[1].stop]
        out.append((min(xs), min(ys), max(xs), max(ys)))
    return out


def group_into_rows(boxes: List[Tuple[int, int, int, int]], rows: int
                    ) -> List[List[Tuple[int, int, int, int]]]:
    """Split figures into `rows` bands by vertical centre, then order each band by x.

    Banding on the CENTRE rather than on the top edge: a dragon rearing up in one frame
    of a row has a much higher top than its neighbours and would sort into the row above.
    """
    if rows <= 1:
        return [sorted(boxes, key=lambda b: (b[0] + b[2]) / 2)]

    by_cy = sorted(boxes, key=lambda b: (b[1] + b[3]) / 2)
    per_row = len(by_cy) // rows
    bands = []
    for r in range(rows):
        lo = r * per_row
        hi = (r + 1) * per_row if r < rows - 1 else len(by_cy)
        bands.append(sorted(by_cy[lo:hi], key=lambda b: (b[0] + b[2]) / 2))
    return bands


def fitted_anchors(boxes: List[Tuple[int, int, int, int]]) -> List[float]:
    """An evenly spaced ladder through the measured figure centres.

    See the module docstring: this stands in for the cell centre the frame builder wants,
    and the difference between a figure's own centre and its rung is the motion.
    """
    centres = [(b[0] + b[2]) / 2.0 for b in boxes]
    n = len(centres)
    if n == 1:
        return centres
    pitch = (centres[-1] - centres[0]) / (n - 1)
    return [centres[0] + i * pitch for i in range(n)]


def slice_sheet(keyed: Image.Image, source_path: str, rows: int, expect: int) -> dict:
    a = np.asarray(keyed)
    boxes = find_figures(a[:, :, 3])

    if len(boxes) != expect:
        raise SystemExit(
            f"{os.path.basename(source_path)}: segmented {len(boxes)} figures but the config "
            f"declares {expect}. The count in the config is a CHECK, not an override — a "
            "mismatch means the keying or the art is not what either side thinks it is.")

    bands = group_into_rows(boxes, rows)

    items = []
    index = 0
    for row, band in enumerate(bands):
        anchors = fitted_anchors(band)
        for box, anchor_x in zip(band, anchors):
            items.append({
                "index": index,
                "row": row,
                "sheet_box": [int(box[0]), int(box[1]), int(box[2]), int(box[3])],
                "anchor_x": round(float(anchor_x), 2),
            })
            index += 1

    return {"source": source_path.replace("\\", "/"), "rows": rows,
            "cols": max(len(b) for b in bands), "items": items}


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--config", required=True)
    ap.add_argument("--src", required=True, help="Folder of <monster>/<sheet>.png sources")
    ap.add_argument("--out", required=True, help="Where keyed sheets and slices are written")
    args = ap.parse_args()

    with open(args.config, encoding="utf-8") as fh:
        config = json.load(fh)

    written = 0
    for key, cfg in config["monsters"].items():
        src_dir = os.path.join(args.src, cfg["sourceDir"])
        out_dir = os.path.join(args.out, key)
        os.makedirs(out_dir, exist_ok=True)

        for stem, grid in cfg["sheets"].items():
            src = os.path.join(src_dir, stem + ".png")
            if not os.path.exists(src):
                raise SystemExit(f"{key}: no sheet at {src}")

            im = Image.open(src)
            # An RGBA sheet already carries real transparency; re-keying it would be
            # second-guessing the artist and would eat any deliberately pale pixel.
            keyed = im.convert("RGBA") if im.mode == "RGBA" else key_out_checkerboard(im)

            keyed_path = os.path.join(out_dir, stem + ".png")
            keyed.save(keyed_path)

            slices = slice_sheet(keyed, os.path.abspath(keyed_path),
                                 int(grid.get("rows", 1)), int(grid["frames"]))
            with open(os.path.join(out_dir, stem + ".slices.json"), "w", encoding="utf-8") as fh:
                json.dump(slices, fh, indent=2)

            written += 1
            print(f"  {key}/{stem}: {len(slices['items'])} frames in "
                  f"{slices['rows']} row(s)  ({im.mode} -> RGBA)")

    print(f"sliced {written} sheet(s)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
