#!/usr/bin/env python3
"""Measure each vampire sheet's ZOOM off the character's FACE.

Why this exists
---------------
``build_player_frames.build_state`` sizes a state from ONE frame's foot-to-crown
height -- frame 0 unless ``REFERENCE_FRAME`` says otherwise -- because a neutral
standing pose is the only frame whose height means "how big is this character".
That assumption holds for wave4 and it breaks on this wave, in both of the ways it
can break at once: every sheet is drawn at its own ZOOM, and several open on a pose
that is not standing, so the single number the reference is read from carries the
two errors mixed together and no statistic over the silhouette separates them.
Measured over the eighteen staged sheets, frame 0's foot-to-crown runs from 279px
(the running strike, which opens mid-lunge) to 676px (the second idle).

What is measured, and why the face
----------------------------------
The FACE. Specifically the height of the largest skin-coloured connected component
in the top sixth of the body: crown-of-face to chin. Three properties make it the
right feature on this art and each was checked rather than assumed.

* It is invariant to YAW. This character is drawn front-on in some sheets and in
  full profile in others, which changes a face's WIDTH and leaves its HEIGHT alone.
* It is invariant to the pose. A crouch, a lunge and a stride all leave the head
  the same size, which is the whole reason CLAUDE.md's note on SCALE_OVERRIDE says
  to "match the head, not the bounding box".
* It is stable WITHIN a sheet, which is the check that it is a measurement rather
  than a coincidence: measured per frame, ``vampire_walking`` reads 50 49 48 48 47
  47 48 49 and ``vampire_running`` reads 43 44 43 43 43 43 44 42.

Two earlier methods were tried and are recorded here so nobody re-derives them:

* HEAD CROSS-CORRELATION, the way the barbarian's numbers were measured. It fails
  its own control on this art -- the idle sheet scored 0.98 against ITSELF and
  ``vampire_idle_2`` came back 0.92 where the frame-0 heights of the two idles
  answer 1.030 independently. The hair is most of the head's silhouette here and
  moves every frame, so the scale search rails to the low end of its sweep on a
  spurious match.
* SAME CANVAS, SAME ZOOM -- the structural argument that sheets rendered onto one
  canvas with one cell size were rendered as a batch. It is directionally right and
  too loose to ship: ``vampire_walking`` and ``vampire_running`` share a 312.5x724
  cell and their faces differ by 11%.

The control this one passes: ``vampire_punch``, whose frame 0 is a clean standing
pose and which therefore already knows its own answer, comes back at **0.998**
without having been used to calibrate anything.

The caveat worth knowing: a head TILTED back or down shortens the measured face, so
a sheet is summarised by a high percentile of its frames rather than by their mean.
Tilt only ever shortens, never lengthens.

Output
------
The SCALE_OVERRIDE table ``build_player_frames`` applies on top of its automatic
frame-0 answer:

    implied_standing_height = faceH / (reference_faceH / reference_standing_height)
    override                = frame0_height / implied_standing_height

Usage
-----
    python tools/atlas/wave11/calibrate_vampire_scale.py [--slices staging/_slices]
"""

from __future__ import annotations

import argparse
import importlib.util
import json
import os
import sys

import numpy as np
from PIL import Image
from scipy import ndimage

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
BUILDER = os.path.join(REPO, "tools", "atlas", "wave3", "build_player_frames.py")

# The idle sheet defines the character: TARGET_BODY_PX is what its standing height
# becomes, so its own override is 1.000 by construction and every other sheet is
# measured against it.
REFERENCE_SHEET = "vampire_idle"

# The band the face is looked for in, as a fraction of the frame's foot-to-crown
# height. A sixth reaches the chin on every pose in the wave and stops above the
# chest, which matters: the neck runs into the collarbones and the shoulders, and a
# blob that swallows them is measuring the pose instead of the head.
FACE_BAND = 0.18
# A blob smaller than this is a knuckle, an ear seen alone, or a sliver of shoulder
# through the hair -- not a face. Measured, a real face is 250-1600px here.
MIN_FACE_AREA = 60
# Tilt only ever SHORTENS a measured face, so the summary leans high. The median of
# the upper half is high enough to discount a head thrown back and low enough that
# one over-merged blob cannot set the answer on its own.
UPPER_HALF = 0.5

SKIN_MIN_ALPHA = 190


def load_builder():
    spec = importlib.util.spec_from_file_location("build_player_frames", BUILDER)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def skin_mask(patch: np.ndarray) -> np.ndarray:
    """Pale skin, and deliberately NOT the cape.

    The cape is the one other large warm region on this character and it separates
    cleanly on GREEN: it is a saturated dark red, so its green channel sits far
    below the skin's. The remaining conditions keep the ordering R >= G >= B that
    every skin tone in this palette has and the crimson trim does not.
    """
    r, g, b, a = (patch[..., i].astype(int) for i in range(4))
    return ((a > SKIN_MIN_ALPHA) & (r > 170) & (g > 135) & (b > 125)
            & (r >= g) & (g + 8 >= b))


def face_heights(slices_root: str, stem: str, mod) -> list[int]:
    """Crown-of-face to chin, in SOURCE pixels, once per frame that shows a face."""
    with open(os.path.join(slices_root, stem + ".slices.json"), encoding="utf-8") as fh:
        manifest = json.load(fh)
    sheet = np.asarray(Image.open(manifest["source"]).convert("RGBA"))

    heights: list[int] = []
    for item in sorted(manifest["items"], key=lambda i: i["index"]):
        x0, y0, x1, y1 = item["sheet_box"]
        patch = sheet[y0:y1, x0:x1]
        rows = np.nonzero((patch[..., 3] > SKIN_MIN_ALPHA).sum(axis=1) > 0)[0]
        if not rows.size:
            continue
        top = int(rows.min())
        body_h = patch.shape[0] - top
        band = patch[top:top + max(6, int(body_h * FACE_BAND))]

        labels, n = ndimage.label(skin_mask(band), structure=np.ones((3, 3)))
        if n == 0:
            continue
        sizes = np.bincount(labels.ravel())[1:]
        if sizes.max() < MIN_FACE_AREA:
            continue
        ys, _xs = np.nonzero(labels == int(sizes.argmax()) + 1)
        heights.append(int(ys.max() - ys.min() + 1))
    return heights


def summarise(heights: list[int]) -> float:
    ordered = sorted(heights, reverse=True)
    keep = ordered[:max(1, int(round(len(ordered) * UPPER_HALF)))]
    return float(np.median(keep))


def frame0_height(slices_root: str, stem: str, mod) -> int:
    with open(os.path.join(slices_root, stem + ".slices.json"), encoding="utf-8") as fh:
        manifest = json.load(fh)
    sheet = np.asarray(Image.open(manifest["source"]).convert("RGBA"))
    item = min(manifest["items"], key=lambda i: i["index"])
    x0, y0, x1, y1 = item["sheet_box"]
    patch = sheet[y0:y1, x0:x1].copy()
    patch[..., 3] = np.where(patch[..., 3] < mod.ALPHA_KEEP, 0, patch[..., 3])
    return mod.foot_line(patch) - mod.body_box(patch)[1]


def measure(slices_root: str) -> None:
    mod = load_builder()
    stems = sorted(s[:-len(".slices.json")] for s in os.listdir(slices_root)
                   if s.startswith("vampire_") and s.endswith(".slices.json"))

    ref_face = summarise(face_heights(slices_root, REFERENCE_SHEET, mod))
    ref_body = frame0_height(slices_root, REFERENCE_SHEET, mod)
    per_px = ref_face / ref_body

    print("reference {0}: face {1:.1f}px over a {2}px standing body "
          "({3:.4f} face px per body px)\n".format(
              REFERENCE_SHEET, ref_face, ref_body, per_px))
    print("{0:32s} {1:>2s} {2:>6s} {3:>8s} {4:>5s} {5:>9s}   per-frame faces".format(
        "sheet", "n", "face", "implied", "f0", "override"))

    for stem in stems:
        heights = face_heights(slices_root, stem, mod)
        if not heights:
            print("{0:32s}  -- no face found in any frame".format(stem))
            continue
        face = summarise(heights)
        implied = face / per_px
        f0 = frame0_height(slices_root, stem, mod)
        print("{0:32s} {1:2d} {2:6.1f} {3:8.0f} {4:5d} {5:9.3f}   {6}".format(
            stem, len(heights), face, implied, f0, f0 / implied,
            " ".join(str(v) for v in heights)))


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--slices", default=os.path.join(REPO, "staging", "_slices"))
    args = ap.parse_args()
    measure(args.slices)
    return 0


if __name__ == "__main__":
    sys.exit(main())
