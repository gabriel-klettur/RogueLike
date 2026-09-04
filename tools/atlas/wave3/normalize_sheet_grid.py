#!/usr/bin/env python3
"""Re-space an unevenly laid out character sheet onto an even grid.

WHY THIS EXISTS
---------------
``build_player_frames.py`` assumes the staged sheet is an even grid. It says so:
the cell of a frame comes from ``divmod(index, cols)`` and the horizontal anchor
is ``(col + 0.5) * cell_w``, one uniform cell width across the whole sheet. Two
things follow, and both are silent.

* ``own_object_only`` rejects a neighbouring frame's fragments by asking whether
  they are centred in THIS cell. With uneven gaps a neighbour's body can sit
  inside this frame's nominal cell, and it is kept.
* Every frame is anchored on its cell centre, so a frame whose body sits far from
  that centre is pushed out to one side, and the canvas has to grow to hold the
  worst case.

Measured on ``knight_mining`` before this tool existed: the eight frames are
272/224/232/205/254/229/234/273 px wide with gaps running 78, 74, 52, 24, 18, 29,
32 -- so the last gaps are a fraction of the first. The build came out as a
450x227 canvas (against 82-195 for every evenly drawn sheet in the project), the
bodies drifted right across the row, and frame 7 contained TWO dwarves because
frame 6 fell inside its cell. Nothing failed; the frames simply came out wrong,
and only a contact sheet showed it.

WHAT IT DOES
------------
Segments the sheet into frames on its alpha gaps, then rebuilds it with one
uniform cell per frame:

* every frame's BODY is centred in its cell (see ``--anchor``);
* the cell is wide enough for the widest frame plus a margin, so no part of a
  frame can reach into its neighbour's cell.

VERTICAL POSITION IS LEFT EXACTLY AS DRAWN, on purpose. The ground line has one
owner and it is ``build_player_frames``: it derives one per ROW from the frames'
own foot lines, reserves nothing below it, and lands the sprite pivot on it. A
second alignment pass here would be a second opinion about the same row, and the
first version of this tool proved why that is dangerous — it aligned on each
frame's own ``foot_line``, which on ``knight_mining`` frame 5 measures the
pickaxe lying on the floor rather than the boots (626 against the sheet's 580).
Every other frame was lifted 46-48px and that one was not, so the character sank
by a sixth of his body on exactly the frame the pick lands. The builder handles
that case declaratively instead: see ``MEDIAN_GROUND_SHEETS``.

ANCHORING, AND WHEN NOT TO USE THIS
-----------------------------------
``--anchor body`` (the default) centres each frame's body in its cell. That is
right for a sheet of separate poses around a stationary character -- a miner
swinging at a rock face, a smith at an anvil -- and WRONG for a cycle that
deliberately translates. ``wave2/build_knight_frames.py`` records the same trade
from the other side: it anchors on the cell centre precisely so the walk keeps its
hip sway and the lunge keeps its lunge. Centring the body on a walk cycle cancels
exactly the motion the animation is made of.

``--anchor bbox`` keeps each frame where it was drawn relative to its own run and
only re-spaces the runs evenly. Use it when the sheet already reads correctly and
only the spacing is uneven.

USAGE
-----
    python tools/atlas/wave3/normalize_sheet_grid.py \
        staging/players/knight_wave4/knight_mining.png --in-place

    # inspect first
    python tools/atlas/wave3/normalize_sheet_grid.py <sheet> --out /tmp/check.png --report
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

import numpy as np
from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parent))
sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from build_player_frames import body_mask, foot_line  # noqa: E402
from slice_prop_sheet import (  # noqa: E402
    DEFAULTS, SheetConfig, detect_boxes, merge_overlapping,
)

#: Alpha at or above which a pixel counts as part of a frame when falling back to
#: the column scan. Deliberately low: these sheets carry soft edges and a faint
#: glow, and cutting on a high threshold shaves the antialiased rim off every
#: silhouette.
ALPHA_FLOOR = 8

#: Empty margin left either side of the widest REACH, in px of the source sheet.
#: It is what guarantees no frame can reach into its neighbour's cell, which is
#: the failure this tool exists to prevent.
CELL_MARGIN = 24


def segment_frames(rgba: np.ndarray) -> list[tuple[int, int]]:
    """Frame spans as (x0, x1) inclusive, in left-to-right order.

    Delegates to ``slice_prop_sheet``'s segmentation rather than scanning for
    blank columns. That is not tidiness: a blank-column scan cannot separate two
    frames whose gap is a few pixels, and ``knight_lumberjack`` has a 5px gap
    between its last two poses. Measured, the column scan merged them into one
    542px box and this tool then wrote a SEVEN-cell sheet over an eight-frame
    animation -- in place, over the only copy. The distance-transform pass
    resolves both cleanly, and it is the same segmentation the rest of the
    pipeline will use on this sheet minutes later, so the two cannot disagree
    about how many frames there are.
    """
    cfg = SheetConfig(name="normalize", params=dict(DEFAULTS))
    boxes = detect_boxes(rgba[:, :, 3], cfg)
    boxes, _ = merge_overlapping(boxes, cfg.param("merge_overlap"))

    # Strictly left to right. reading_order would cluster rows, and these sheets
    # are exactly the ones whose crouched poses cluster as a bogus second row --
    # see knight_harvest.slices.json. One row is this tool's whole premise, so it
    # sorts on x and leaves row inference to the slicer, which can be told better.
    spans = sorted((b.x0, b.x1 - 1) for b in boxes)
    return [(int(a), int(b)) for a, b in spans]


def stance_centre(rgba: np.ndarray, span: tuple[int, int]) -> float:
    """Where the character STANDS, in sheet x, for one frame.

    Not the bounding box centre and not the whole silhouette's centroid: a
    pickaxe held out to one side drags either of those with it, so the character
    would slide across its cell as the tool swings while the art has him rooted.
    The boots are wide, solid, and where the character actually is.

    ``foot_line`` is used only to find the band to measure IN. Its blind spot -- a
    low horizontal weapon reading as a boot -- costs a few px of horizontal
    centring here, where in a vertical alignment it would cost a sixth of the
    body. That is why this tool measures across and the builder measures down.
    """
    x0, x1 = span
    patch = rgba[:, x0 : x1 + 1]

    mask = body_mask(patch)
    if mask is None:
        alpha = patch[:, :, 3] >= ALPHA_FLOOR
        xs = np.nonzero(alpha.any(axis=0))[0]
        return x0 + (float(xs.mean()) if xs.size else 0.0)

    foot = foot_line(patch) or mask.shape[0]
    band = max(4, int(mask.shape[0] * 0.10))
    stance = mask[max(0, foot - band) : foot]

    xs = np.nonzero(stance.any(axis=0))[0]
    if xs.size == 0:
        xs = np.nonzero(mask.any(axis=0))[0]
    return x0 + float(xs.mean())


def normalize(sheet: Image.Image, anchor: str, report: bool):
    rgba = np.array(sheet.convert("RGBA"))
    spans = segment_frames(rgba)
    if len(spans) < 2:
        raise SystemExit("found fewer than two frames -- is this a sheet?")

    centres = [stance_centre(rgba, sp) for sp in spans]

    # The cell has to be wide enough to CENTRE the most lopsided pose, not merely
    # to contain the widest one. A miner holding the pick out to one side reaches
    # much further left of his boots than right, and sizing on the bounding box
    # leaves less slack than the centring needs -- the frame then has to be shoved
    # back inside its cell, which un-centres it and makes the builder's canvas grow
    # to hold the worst offender. Measured on knight_mining: bbox sizing gave a
    # 493px canvas with frame 0 flush against its own cell wall.
    reach = 0.0
    for (x0, x1), cx in zip(spans, centres):
        reach = max(reach, cx - x0, x1 + 1 - cx)
    cell_w = int(round(reach * 2)) + CELL_MARGIN * 2
    out_w = cell_w * len(spans)

    # Same height, and every frame keeps the row it was drawn on. The builder owns
    # the ground line; see the module docstring.
    out = Image.new("RGBA", (out_w, sheet.height), (0, 0, 0, 0))

    for i, (x0, x1) in enumerate(spans):
        crop = sheet.crop((x0, 0, x1 + 1, sheet.height))

        if anchor == "body":
            offset_x = int(round(i * cell_w + cell_w / 2.0 - (centres[i] - x0)))
        else:
            offset_x = int(round(i * cell_w + (cell_w - crop.width) / 2.0))

        # The cell was sized so this cannot happen; assert rather than clamp,
        # because clamping would silently un-centre the frame it rescued.
        assert i * cell_w <= offset_x and offset_x + crop.width <= (i + 1) * cell_w,             f"frame {i} does not fit its cell -- raise CELL_MARGIN"

        out.alpha_composite(crop, (offset_x, 0))

        if report:
            print(f"  frame {i}: src x{x0}-{x1} w{x1 - x0 + 1:3d} "
                  f"-> cell x{i * cell_w} offset {offset_x:5d}")

    if report:
        print(f"  grid: {len(spans)} cells of {cell_w}px, sheet {out_w}x{sheet.height}")

    return out, len(spans)


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("sheet", type=Path)
    ap.add_argument("--out", type=Path, help="write here instead of in place")
    ap.add_argument("--in-place", action="store_true")
    ap.add_argument("--anchor", choices=("body", "bbox"), default="body",
                    help="body: centre the stance in each cell (poses). "
                         "bbox: keep each frame as drawn, only re-space (cycles).")
    ap.add_argument("--expect", type=int,
                    help="refuse to write unless exactly this many frames are found")
    ap.add_argument("--report", action="store_true")
    args = ap.parse_args()

    if not args.out and not args.in_place:
        ap.error("pass --out or --in-place")

    sheet = Image.open(args.sheet).convert("RGBA")
    print(f"{args.sheet.name}: {sheet.width}x{sheet.height}")

    out, found = normalize(sheet, args.anchor, args.report)

    # An in-place write over a staged sheet is over the only copy: staging/ is
    # gitignored, so a wrong segmentation here is not recoverable from the repo.
    if args.expect is not None and found != args.expect:
        raise SystemExit(f"found {found} frames, expected {args.expect} -- refusing to write")

    dest = args.sheet if args.in_place else args.out
    if args.in_place:
        backup = args.sheet.with_suffix(args.sheet.suffix + ".bak")
        if not backup.exists():
            Image.open(args.sheet).save(backup)
            print(f"backed up original to {backup.name}")
    out.save(dest)
    print(f"wrote {dest} ({out.width}x{out.height})")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
