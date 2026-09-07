#!/usr/bin/env python3
"""Turn the wave13 monster sheets into clean RGBA, and report the grid each one uses.

WHY THIS STEP EXISTS
--------------------
Two of the three monsters in this wave (`barbol_muscle`, `barbol_young`) ship as
RGB PNGs with the editor's transparency CHECKERBOARD baked into the pixels. They
have no alpha channel at all. Every downstream tool in this repo segments on
alpha -- `slice_prop_sheet.py` thresholds it, `build_monster_frames.py` finds the
body box with it, and Unity's sprite import assumes it -- so feeding these
sheets in directly produces one frame per sheet, the size of the sheet, with a
grey chequered background welded to the character.

The keying is a BORDER FLOOD FILL, not a global threshold, and that is the whole
design. The checkerboard is near-white and achromatic (measured 247-253 on all
three channels), and so are a barbol's teeth, the whites of its eyes and the
brightest specular on its belt bead. A global "bright pixels are background"
rule punches holes through exactly those, and the holes are small enough to
survive review and obvious in game. Background is by definition the region
connected to the sheet border; an eye is not.

The antialiased rim is given PARTIAL alpha rather than being cut hard or kept
opaque. A hard cut leaves a one-pixel white halo that survives downscaling as a
bright fringe; keeping it opaque welds the same halo on permanently. Ramping
alpha across the blend band is what makes the silhouette read clean at the sizes
these are actually drawn at.

GRID DETECTION
--------------
The sheets are NOT one layout. Measured across this wave: most barbol sheets are
6x1, the dragon's idle and walk are 3x2, and its attack is 4x2 -- and several
barbol sheets are a different pixel size again. `build_monster_frames.py` takes
`cols`/`rows` per state as explicit config, so this script reports what it
measures and the config is written from the measurement rather than from an
assumption about the wave.

The measurement is a projection: a column with no opaque pixel anywhere in it is
a gutter, and the runs of non-gutter columns are the frames. Same for rows. That
is robust to the frames being different widths, which they are -- a roaring
barbol is wider than a standing one.

Usage
-----
    python dechecker_and_probe.py --src <dir> --out <dir>
    python dechecker_and_probe.py --src <dir> --out <dir> --report-only
"""

from __future__ import annotations

import argparse
import json
import os
import sys
from typing import List, Tuple

import numpy as np
from PIL import Image
from scipy import ndimage

# The checkerboard measured on this wave: every channel at or above this, and
# near-grey. Deliberately generous on the low end (the darker checker square is
# ~247) and tight on the colour spread, because what separates the background
# from a highlight here is that the background has no hue at all.
BG_MIN_LEVEL = 200
BG_MAX_CHROMA = 22

# Where the antialiased rim stops being background and starts being character.
# Alpha ramps linearly between these two luminances.
RIM_OPAQUE_AT = 200.0
RIM_CLEAR_AT = 250.0

# A gutter column/row is one with no pixel at least this opaque.
GUTTER_ALPHA = 8


def key_out_checkerboard(im: Image.Image) -> Image.Image:
    """RGB with a baked checkerboard -> RGBA with the checkerboard removed."""
    rgb = np.asarray(im.convert("RGB")).astype(np.int16)
    h, w, _ = rgb.shape

    lo = rgb.min(axis=2)
    hi = rgb.max(axis=2)
    bright_grey = (lo >= BG_MIN_LEVEL) & ((hi - lo) <= BG_MAX_CHROMA)

    # Only the region reachable from the border is background. An eye white is
    # bright and grey and is not reachable, which is the point.
    labels, count = ndimage.label(bright_grey)
    if count == 0:
        out = np.dstack([rgb.astype(np.uint8),
                         np.full((h, w), 255, np.uint8)])
        return Image.fromarray(out, "RGBA")

    border = set(labels[0, :].tolist()) | set(labels[-1, :].tolist())
    border |= set(labels[:, 0].tolist()) | set(labels[:, -1].tolist())
    border.discard(0)

    background = np.isin(labels, list(border))

    # Soft rim: a pixel that is NOT background but sits in the blend band gets
    # partial alpha, scaled by how far its luminance is from the plate.
    luma = rgb.mean(axis=2)
    alpha = np.full((h, w), 255.0)
    ramp = np.clip((RIM_CLEAR_AT - luma) / (RIM_CLEAR_AT - RIM_OPAQUE_AT), 0.0, 1.0)
    near_bg = ndimage.binary_dilation(background, iterations=2) & ~background
    alpha[near_bg] = ramp[near_bg] * 255.0
    alpha[background] = 0.0

    out = np.dstack([rgb.astype(np.uint8), alpha.astype(np.uint8)])
    return Image.fromarray(out, "RGBA")


def runs(occupied: np.ndarray) -> List[Tuple[int, int]]:
    """Contiguous [start, end) runs of True."""
    out: List[Tuple[int, int]] = []
    start = None
    for i, v in enumerate(occupied):
        if v and start is None:
            start = i
        elif not v and start is not None:
            out.append((start, i))
            start = None
    if start is not None:
        out.append((start, len(occupied)))
    return out


def _row_bands(solid: np.ndarray) -> List[Tuple[int, int]]:
    """Horizontal bands of content. Rows are the easy axis: nothing on this wave
    reaches vertically out of its own row."""
    bands = runs(solid.any(axis=1))
    # Discard slivers: a stray antialiased pixel between two rows is not a row.
    height = solid.shape[0]
    return [b for b in bands if (b[1] - b[0]) > height * 0.08]


def _best_column_count(profile: np.ndarray, max_cols: int = 10) -> Tuple[int, float]:
    """How many frames a row band holds.

    A PROJECTION IS NOT ENOUGH HERE, and that is worth stating because it is the
    obvious method and it was tried first: it looks for columns with no opaque
    pixel at all, and on this wave the frames OVERLAP -- a roaring barbol's arm
    reaches into its neighbour's column, a dragon's wing and tail cross two.
    Measured, the projection reported 5 frames for a sheet that visibly holds 6
    and disagreed with itself between the dragon's two rows (3 and then 5).

    What survives overlap is PERIODICITY. Frames are evenly spaced whether or not
    they touch, so the right column count is the one whose evenly-spaced cut lines
    land in the thinnest part of the profile. Scored as the mean density ON the
    cut lines divided by the mean density overall, so a sheet whose frames are
    merely close still scores far better at its true period than at any other.

    Not required to divide the width evenly: `red_dragon_attack` is 1774 px wide
    holding 4 columns, which is 443.5 each.
    """
    total = float(profile.sum())
    if total <= 0:
        return 1, 1.0

    width = len(profile)
    mean = total / width
    best_n, best_score = 1, float("inf")

    for n in range(1, max_cols + 1):
        if n > 1 and width / n < 24:
            continue

        # The interior cut lines, sampled with a small window because a gutter is
        # never exactly one pixel wide once the art has been resampled.
        cut_density = []
        for k in range(1, n):
            x = int(round(k * width / n))
            lo, hi = max(0, x - 3), min(width, x + 4)
            cut_density.append(profile[lo:hi].mean())

        if not cut_density:
            score = 1.0                      # n == 1 is the fallback, never preferred
        else:
            score = float(np.mean(cut_density)) / max(mean, 1e-6)

            # Every cell must actually hold something. A count that splits one
            # frame in half scores well on the cuts and is plainly wrong.
            empty = 0
            for k in range(n):
                a = int(round(k * width / n))
                b = int(round((k + 1) * width / n))
                if profile[a:b].sum() < total * 0.02:
                    empty += 1
            if empty:
                score += empty * 10.0

            # Mild preference for fewer frames, so a true 4 is not beaten by 8
            # cutting each frame down the middle at a slightly thinner place.
            score += n * 0.012

        if score < best_score:
            best_n, best_score = n, score

    return best_n, best_score


def probe_grid(im: Image.Image) -> dict:
    a = np.asarray(im)
    alpha = a[:, :, 3].astype(np.float32)
    solid = alpha >= GUTTER_ALPHA

    bands = _row_bands(solid)
    if not bands:
        return {"size": [im.size[0], im.size[1]], "rows": 0, "cols": 0}

    per_row = []
    for (r0, r1) in bands:
        profile = alpha[r0:r1].sum(axis=0)
        n, score = _best_column_count(profile)
        per_row.append((n, round(score, 4)))

    cols = max(n for n, _ in per_row)
    return {
        "size": [im.size[0], im.size[1]],
        "rows": len(bands),
        "cols": cols,
        "colsPerRow": [n for n, _ in per_row],
        "scorePerRow": [s for _, s in per_row],
        "rowBands": [[int(x), int(y)] for x, y in bands],
    }


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--src", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--report", default="")
    ap.add_argument("--report-only", action="store_true")
    args = ap.parse_args()

    report = {}
    for monster in sorted(os.listdir(args.src)):
        mdir = os.path.join(args.src, monster)
        if not os.path.isdir(mdir):
            continue
        outdir = os.path.join(args.out, monster)
        if not args.report_only:
            os.makedirs(outdir, exist_ok=True)

        report[monster] = {}
        for name in sorted(os.listdir(mdir)):
            if not name.lower().endswith(".png"):
                continue
            path = os.path.join(mdir, name)
            im = Image.open(path)

            # An RGBA sheet already carries real transparency. Re-keying it
            # would be second-guessing the artist and would eat any deliberately
            # pale pixel; only the RGB ones need the plate removed.
            keyed = im.convert("RGBA") if im.mode == "RGBA" else key_out_checkerboard(im)

            grid = probe_grid(keyed)
            grid["source_mode"] = im.mode
            report[monster][os.path.splitext(name)[0]] = grid

            if not args.report_only:
                keyed.save(os.path.join(outdir, name))

    text = json.dumps(report, indent=2)
    if args.report:
        os.makedirs(os.path.dirname(args.report), exist_ok=True)
        with open(args.report, "w", encoding="utf-8") as fh:
            fh.write(text)
    print(text)
    return 0


if __name__ == "__main__":
    sys.exit(main())
