#!/usr/bin/env python3
"""Compose the vampire's card art for the character-selection screen.

This is the TEMPORARY half of the vampire import, and it is worth saying what it
stands in for. The five shipped ``character_selection_*.png`` are not portraits:
each is the same painted tavern group with a DIFFERENT one of the five characters
lit, so selecting a class re-lights one figure in a scene the other four are also
standing in. A sixth character cannot join that scene without the scene being
repainted, and repainting it is not something an import script can do.

So this composes the next-best thing out of art that exists: the shipped empty
tavern (``taberna.png``, the same 1536x1024 plate the group images are painted
over) with the vampire's own idle frame standing in it, graded down to the room's
firelight and given the crimson rim the other cards give their selected figure.
It reads as a deliberate card rather than a placeholder, and it is one file to
delete when the group plate is repainted with six.

Three things are done to the cut-out and each is why it does not look pasted on:

* GRADE. The sheet is lit flat and cool; the room is lit by one fire, warm and
  two stops down. The figure is multiplied toward the room's own median colour
  and lifted slightly where the fire would reach her, so she shares the room's
  black point instead of floating in front of it.
* CONTACT. Nothing anchors a standing figure to a floor like the shadow it casts,
  and a cut-out with none reads as a sticker. A soft elliptical shadow is laid at
  the boot line before she is composited.
* RIM. An OUTER glow in crimson, drawn under her, which is the same statement
  the knight's blue glow makes on his card: this is the one that is selected.
  It is the blurred silhouette MINUS the silhouette itself, and the subtraction
  is the whole trick: this character's hair and cape fill 61% of her own bounding
  box, so a blur alone comes back at full alpha almost everywhere inside it and
  the glow renders as a soft RECTANGLE around her. Subtracting her own alpha
  leaves only what lies outside the silhouette, which is what a rim is.

Usage
-----
    python tools/atlas/wave11/build_vampire_portrait.py [--dry-run]
"""

from __future__ import annotations

import argparse
import json
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
RESOURCES = os.path.join(REPO, "unity", "Valkur", "Assets", "_Project", "Resources",
                         "UI", "CharacterSelection")
TAVERN = os.path.join(RESOURCES, "taberna.png")
OUT = os.path.join(RESOURCES, "character_selection_vampire.png")

SLICES = os.path.join(REPO, "staging", "_slices")
# Frame 3 of the idle: both arms down, weight even, the cape hanging rather than
# swinging. The frames where a hand is raised to the hair read as a gesture caught
# mid-way, which is right in an animation and wrong in a still.
SOURCE_SHEET = "vampire_idle"
SOURCE_FRAME = 3

# Where she stands, as fractions of the plate. The floor's front edge runs across
# the lower fifth of the tavern and the table occupies its left half, so she goes
# right of centre with her boots on the open flagstones.
FEET_Y = 0.990
CENTRE_X = 0.560
# Height as a fraction of the plate, boots to crown. She stands in the FOREGROUND
# rather than in the room's own depth, which is what lets her cross the table edge
# without the composite having to decide what is in front of what.
BODY_FRACTION = 0.88

# The grade. TINT is multiplied in; LIFT is added after, so the black point rises
# to the room's rather than staying at the sheet's pure black.
TINT = (0.60, 0.47, 0.45)
LIFT = (0.055, 0.030, 0.032)
RIM_COLOR = (215, 45, 75)
RIM_DILATE = 5
RIM_BLUR = 18
RIM_ALPHA = 0.85
SHADOW_ALPHA = 0.60


def load_frame() -> Image.Image:
    with open(os.path.join(SLICES, SOURCE_SHEET + ".slices.json"), encoding="utf-8") as fh:
        manifest = json.load(fh)
    sheet = Image.open(manifest["source"]).convert("RGBA")
    item = next(i for i in manifest["items"] if i["index"] == SOURCE_FRAME)
    x0, y0, x1, y1 = item["sheet_box"]
    crop = sheet.crop((x0, y0, x1, y1))
    # Trim to the frame's own alpha so BODY_FRACTION means the body and not the
    # slack the segmentation box carries around it.
    bbox = crop.getchannel("A").point(lambda v: 255 if v > 16 else 0).getbbox()
    return crop.crop(bbox)


def graded(figure: Image.Image) -> Image.Image:
    arr = np.asarray(figure).astype(np.float32) / 255.0
    rgb, a = arr[..., :3], arr[..., 3:]
    rgb = rgb * np.array(TINT, dtype=np.float32) + np.array(LIFT, dtype=np.float32)
    out = np.concatenate([np.clip(rgb, 0.0, 1.0), a], axis=2)
    return Image.fromarray((out * 255.0 + 0.5).astype(np.uint8), "RGBA")


def rim(figure: Image.Image) -> Image.Image:
    """The outer glow: the blurred silhouette with the silhouette subtracted back out.

    Padded first, because a glow is drawn OUTSIDE the figure and a canvas exactly the
    size of the figure has nowhere to put it -- the halo would be clipped flat against
    the boots and the crown.
    """
    pad = RIM_BLUR * 3
    canvas = Image.new("L", (figure.width + pad * 2, figure.height + pad * 2), 0)
    alpha = figure.getchannel("A")
    canvas.paste(alpha, (pad, pad))
    solid = np.asarray(canvas).astype(np.float32)

    spread = np.asarray(canvas.filter(ImageFilter.MaxFilter(RIM_DILATE))
                              .filter(ImageFilter.GaussianBlur(RIM_BLUR))).astype(np.float32)
    outer = np.clip(spread - solid, 0.0, 255.0) * RIM_ALPHA

    glow = Image.new("RGBA", canvas.size, RIM_COLOR + (0,))
    glow.putalpha(Image.fromarray(outer.astype(np.uint8), "L"))
    return glow


def compose() -> Image.Image:
    plate = Image.open(TAVERN).convert("RGBA")
    pw, ph = plate.size

    figure = graded(load_frame())
    scale = (ph * BODY_FRACTION) / figure.height
    figure = figure.resize((max(1, round(figure.width * scale)),
                            max(1, round(figure.height * scale))), Image.LANCZOS)

    fx = int(pw * CENTRE_X - figure.width / 2)
    fy = int(ph * FEET_Y - figure.height)

    # Contact shadow, drawn on the plate before anything else so the glow sits over it.
    shadow = Image.new("RGBA", plate.size, (0, 0, 0, 0))
    sw = int(figure.width * 0.52)
    sh = max(8, int(figure.height * 0.045))
    cx = fx + figure.width // 2
    cy = int(ph * FEET_Y) - sh // 2
    ImageDraw.Draw(shadow).ellipse(
        [cx - sw // 2, cy - sh // 2, cx + sw // 2, cy + sh // 2],
        fill=(0, 0, 0, int(255 * SHADOW_ALPHA)))
    shadow = shadow.filter(ImageFilter.GaussianBlur(sh * 0.7))
    plate.alpha_composite(shadow)

    glow = rim(figure)
    pad = RIM_BLUR * 3
    plate.alpha_composite(glow, (fx - pad, fy - pad))
    plate.alpha_composite(figure, (fx, fy))
    return plate.convert("RGB")


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--out", default=OUT)
    args = ap.parse_args()

    image = compose()
    if args.dry_run:
        print("would write {0} ({1}x{2})".format(args.out, *image.size))
        return 0
    image.save(args.out)
    print("wrote {0} ({1}x{2})".format(args.out, *image.size))
    return 0


if __name__ == "__main__":
    sys.exit(main())
