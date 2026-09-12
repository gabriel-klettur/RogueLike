#!/usr/bin/env python3
"""Split the valkyrie wave14 two-row EQUIP sheets into one sheet per animation.

Three of the forty-two staged sheets draw TWO INDEPENDENT animations, one per row:
the top row is the two-handed greatsword being drawn, the bottom row is the
sword-and-shield being drawn. Everything downstream treats one sheet as one
animation -- ``build_player_frames.build_state`` reads every item of a sheet in
reading order and emits them as a single frame list, and the state folder it writes
is named after the sheet -- so leaving them joined would ship a sixteen-frame
"equip" that draws a greatsword, throws it away, and then draws a sword and shield.

It is a SOURCE split rather than a builder flag on purpose. The builder already
supports multi-row sheets and has to: ``valkyrie_die`` and ``valkyrie_spellcast_3``
are genuinely one 8-frame animation laid out 4x2, and their rows must stay in one
frame list. A flag saying "this sheet's rows are separate animations" would then be
a second, contradicting answer to what a row means, living one level away from the
thing it describes. Two files, two animations, and every tool downstream needs no
special case.

Where the cut goes
------------------
Halfway down, measured rather than assumed. The solid-alpha row profile of all
three sheets leaves a clear empty band across the middle -- combo 497..643 of 1024,
combo_2 353..401 of 724, combo_3 509..671 of 1024 -- so ``height // 2`` lands inside
the gap in every case and no frame is clipped. The script asserts that instead of
trusting it: a sheet whose halves touch would be cut through a character, and the
failure would be a sliver of boot welded to the top of the other animation's canvas.

Each half keeps the FULL WIDTH and exactly half the height, so the result is a plain
1x8 sheet -- which is what ``infer_grid`` sees in every other sheet of the wave.

The originals are moved into ``_combined_src/`` rather than deleted: they are the
record of how the art arrived, and ``slice_prop_sheet.py --all`` globs the staging
folder non-recursively, so a subfolder is also how they stop being sliced twice.

Usage
-----
    python tools/atlas/wave14/split_valkyrie_combos.py [--dry-run]
"""

from __future__ import annotations

import argparse
import os
import shutil

import numpy as np
from PIL import Image
from scipy import ndimage

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
STAGING = os.path.join(REPO, "staging", "players", "valkyrie_wave14")
ARCHIVE = os.path.join(STAGING, "_combined_src")

# The solid-alpha threshold ``slice_prop_sheet`` segments on. Using the same number
# keeps "is the middle band empty" the same question the slicer will ask.
ALPHA_SOLID = 190

# source sheet -> (top row's shipped stem, bottom row's shipped stem)
#
# The row order is the art's, not a convention: in all three sheets the top row is
# the greatsword and the bottom row the sword and shield. Verified frame by frame at
# 6x before these names were written down -- what looks like a second blade in the
# tail frames of the greatsword rows is the NEXT frame's sword leaning back across
# the cell boundary, which ``own_object_only`` removes, and not a dual-wield pose.
SPLITS: dict[str, tuple[str, str]] = {
    "valkyrie_equip_combo":   ("valkyrie_greatsword_equip_2", "valkyrie_shield_equip_3"),
    "valkyrie_equip_combo_2": ("valkyrie_greatsword_equip_3", "valkyrie_shield_equip_4"),
    "valkyrie_equip_combo_3": ("valkyrie_greatsword_equip_4", "valkyrie_shield_equip_5"),
}


def empty_band(alpha: np.ndarray) -> tuple[int, int] | None:
    """The widest fully transparent horizontal band, or None if the rows touch."""
    occupied = (alpha >= ALPHA_SOLID).sum(axis=1) > 0
    labels, count = ndimage.label(~occupied)
    if count == 0:
        return None
    best = None
    for label in range(1, count + 1):
        rows = np.nonzero(labels == label)[0]
        # Skip the margins above the first row and below the last -- they are not a
        # gap BETWEEN two rows, and on a sheet with generous headroom the top margin
        # is the widest empty band on the page.
        if rows[0] == 0 or rows[-1] == alpha.shape[0] - 1:
            continue
        if best is None or rows.size > best[1] - best[0]:
            best = (int(rows[0]), int(rows[-1]))
    return best


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--dry-run", action="store_true",
                    help="Report the cut each sheet would take without writing anything")
    args = ap.parse_args()

    if not os.path.isdir(STAGING):
        print(f"no staging folder at {os.path.relpath(STAGING, REPO)}")
        return 2

    written = 0
    for stem, (top_stem, bottom_stem) in SPLITS.items():
        src = os.path.join(STAGING, f"{stem}.png")
        if not os.path.exists(src):
            archived = os.path.join(ARCHIVE, f"{stem}.png")
            if os.path.exists(archived):
                print(f"{stem}: already split (original in _combined_src/)")
                continue
            print(f"{stem}: MISSING from staging")
            return 2

        image = Image.open(src).convert("RGBA")
        width, height = image.size
        alpha = np.asarray(image)[..., 3]

        gap = empty_band(alpha)
        cut = height // 2
        if gap is None:
            print(f"{stem}: the two rows TOUCH -- no transparent band between them, "
                  f"so a halfway cut would slice through a character. Fix by hand.")
            return 2
        if not gap[0] <= cut <= gap[1]:
            print(f"{stem}: the halfway cut at y={cut} is outside the empty band "
                  f"{gap[0]}..{gap[1]} -- the rows are not evenly placed. Fix by hand.")
            return 2

        print(f"{stem}: {width}x{height}, empty band y {gap[0]}..{gap[1]}, cut at {cut}")
        for name, box in ((top_stem, (0, 0, width, cut)),
                          (bottom_stem, (0, cut, width, height))):
            half = image.crop(box)
            ink = int((np.asarray(half)[..., 3] >= ALPHA_SOLID).sum())
            print(f"    -> {name}.png  {half.size}  {ink} solid px")
            if not args.dry_run:
                half.save(os.path.join(STAGING, f"{name}.png"))
            written += 1

        if not args.dry_run:
            os.makedirs(ARCHIVE, exist_ok=True)
            shutil.move(src, os.path.join(ARCHIVE, f"{stem}.png"))

    verb = "would write" if args.dry_run else "wrote"
    print(f"\n{verb} {written} single-row sheets; originals kept in "
          f"{os.path.relpath(ARCHIVE, REPO)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
