#!/usr/bin/env python3
"""Cut Gatita's 3x3 facial-expression sheet into one aligned PNG per expression.

    python tools/atlas/wave6/build_gatita_faces.py [--dry-run]

WHY THE CROPS ARE NOT TRIMMED TIGHT. These nine sprites are swapped IN PLACE in a
UI panel, one after another, in the same rect. A crop trimmed to its own alpha is
correct for a prop and wrong for a face: the ears, the crown and the chin land a
few pixels apart in every frame, so the head visibly jumps every time the
expression changes. Every face is therefore pasted onto ONE shared canvas.

HOW THEY ARE ALIGNED. Not by the grid — measured, the artist's own cells disagree
by 29 px horizontally and 57 px vertically, and two faces run into the cell edge.
Not by the snout either: the pink mask picks up blush, ear interiors and the
tongue, and its centroid wandered 36 px across the nine.

What does hold is the SILHOUETTE. The ears, crown and head outline are the same
drawing in all nine — only the features inside change — so each face's alpha mask
is cross-correlated against the neutral one and shifted by the integer peak.
Measured NCC 0.87-0.96 with unambiguous peaks; the corrections are at most 23 px.

WHAT IS NOT IN THIS SHEET. `gatita_face_thinking.png` was drawn separately and is not
produced here — the 3x3 grid holds nine faces and cell 3 is the uneasy one, which
ships as `worry`. Re-running this writes the nine below and leaves the thinking face
alone; it must stay on the same 370x395 canvas as the rest or the head jumps when the
panel swaps to it.

The sheet has no `.meta`: Unity has never imported it, so nothing references it and
the cut is free to define the naming. Pivot is CENTRE, unlike Gatita's world
sprites (bottom-centre, PPU 64) — a portrait is placed by its middle, a body stands
on its feet.
"""

from __future__ import annotations

import argparse
import os
import sys

import numpy as np
from PIL import Image
from scipy import signal

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
SHEET = os.path.join(
    REPO_ROOT, "unity", "Valkur", "Assets", "_Project", "Art", "NPC", "neutral",
    "vendors", "cheff", "gatita_chanchita", "gatita_facial_expressions.png")
OUT_DIR = os.path.join(os.path.dirname(SHEET), "facial")
PREFIX = "gatita_face"

# Alpha bands measured off the sheet, row-major. The artist drew on a grid but did
# not align to it, which is why these are measured rather than derived from a pitch.
ROW_BANDS = [(56, 415), (455, 822), (849, 1226)]
COL_BANDS = [(52, 394), (434, 774), (841, 1183)]

# Row-major, matching the bands above. Read off the art, not guessed:
#   neutral  wide open eyes, closed calm mouth, no blush
#   happy    eyes closed in arcs, open smile, tongue, blush
#   laugh    one eye winking, mouth wide open, blush
#   worry    brows flat and slightly furrowed, small closed mouth
#   angry    brows angled down to the centre, eyes narrowed, mouth flat
#   playful  wide eyes, tongue out
#   sad      inner brows raised, eyes turned away, mouth downturned
#   tired    half-lidded eyes, flat mouth
#   wink     one eye winking, closed smile, blush
NAMES = [
    "neutral", "happy", "laugh",
    "worry", "angry", "playful",
    "sad", "tired", "wink",
]

ALPHA_CUTOFF = 60      # what counts as silhouette for the correlation
MARGIN = 8             # transparent border kept around the union of all nine


def load_cells(sheet: Image.Image) -> list[np.ndarray]:
    """The nine crops, row-major, as RGBA arrays at native resolution."""
    px = np.array(sheet.convert("RGBA"))
    cells = []
    for y0, y1 in ROW_BANDS:
        for x0, x1 in COL_BANDS:
            cells.append(px[y0:y1 + 1, x0:x1 + 1])
    return cells


def align(cells: list[np.ndarray]) -> list[tuple[int, int]]:
    """Integer (dx, dy) that puts each cell's silhouette onto the first one's."""
    pad = 64
    h = max(c.shape[0] for c in cells) + 2 * pad
    w = max(c.shape[1] for c in cells) + 2 * pad

    masks = []
    for c in cells:
        canvas = np.zeros((h, w), np.float32)
        canvas[pad:pad + c.shape[0], pad:pad + c.shape[1]] = (c[:, :, 3] > ALPHA_CUTOFF)
        masks.append(canvas - canvas.mean())

    ref = masks[0]
    shifts = []
    for m in masks:
        corr = signal.fftconvolve(ref, m[::-1, ::-1], mode="same")
        iy, ix = np.unravel_index(int(np.argmax(corr)), corr.shape)
        shifts.append((int(ix - corr.shape[1] // 2), int(iy - corr.shape[0] // 2)))
    return shifts


def compose(cells, shifts):
    """Every face on one canvas sized to hold the union of all nine, aligned."""
    # Each cell's own alpha box, moved by its shift, in a shared coordinate space.
    boxes = []
    for c, (dx, dy) in zip(cells, shifts):
        ys, xs = np.nonzero(c[:, :, 3] > 8)
        boxes.append((xs.min() + dx, ys.min() + dy, xs.max() + dx, ys.max() + dy))

    x0 = min(b[0] for b in boxes) - MARGIN
    y0 = min(b[1] for b in boxes) - MARGIN
    x1 = max(b[2] for b in boxes) + MARGIN
    y1 = max(b[3] for b in boxes) + MARGIN
    size = (int(x1 - x0 + 1), int(y1 - y0 + 1))

    out = []
    for c, (dx, dy) in zip(cells, shifts):
        canvas = Image.new("RGBA", size, (0, 0, 0, 0))
        canvas.paste(Image.fromarray(c), (int(dx - x0), int(dy - y0)))
        out.append(canvas)
    return out, size


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    if not os.path.exists(SHEET):
        print(f"ERROR sheet not found: {SHEET}", file=sys.stderr)
        return 1

    cells = load_cells(Image.open(SHEET))
    shifts = align(cells)
    images, size = compose(cells, shifts)

    print(f"{len(images)} faces, shared canvas {size[0]}x{size[1]}")
    os.makedirs(OUT_DIR, exist_ok=True)
    for name, img, (dx, dy) in zip(NAMES, images, shifts):
        path = os.path.join(OUT_DIR, f"{PREFIX}_{name}.png")
        print(f"  {name:9s} shift=({dx:4d},{dy:4d})  -> {os.path.relpath(path, REPO_ROOT)}")
        if not args.dry_run:
            img.save(path)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
