#!/usr/bin/env python3
"""Cut Gatita's idle and walk sheets into per-frame PNGs the entity animator can use.

The sheets are AI renders and are NOT on a grid: cell widths run 223-344 px, the
character is drawn at a different zoom on every sheet, and one sheet carries a
9-pixel speck of stray colour between two frames.  So frames are found by ALPHA
rather than by dividing the width, and each sheet is normalised independently.

What this does, and why each part is not the obvious thing:

* **One canvas per state, never per frame.**  Trimming each frame to its own alpha
  is the documented way to make a walk cycle jitter and the feet leave the ground
  (see ``tools/atlas/wave2/build_knight_frames.py`` and CLAUDE.md).  Every frame of
  a state is pasted onto one shared canvas.

* **Anchored on the FEET, not on the bounding box.**  Gatita's tail sticks out to
  one side and swings between frames, so centring on the bbox would slide her body
  left and right as the tail moves.  The horizontal anchor is the centre of the
  opaque pixels in the bottom slice of the body — where she is actually standing.

* **The ground line is the lowest row with real horizontal EXTENT**, not the lowest
  opaque pixel.  A dangling tail tip or a stray shadow pixel is a sliver; boots are
  not.  Anchoring on the lowest pixel floats the character whenever the tail dips.

* **Each sheet is scaled by its own MEDIAN body height**, not by frame 0.  Frame 0
  is the project's usual reference and is wrong here: these sheets have no neutral
  opening pose, and the walk's frame 0 is mid-stride.  The median is safe because
  both states are upright throughout — the case the frame-0 rule exists to protect
  against is a sheet that is mostly one unusual pose, like a death lying prone.
  Normalising per sheet removes the between-sheet zoom difference (walk renders
  ~487 px tall, idle_3 ~577) while KEEPING the within-sheet variation, which on an
  idle is the breathing.

* **Resampled in premultiplied alpha.**  These sheets have un-premultiplied edges —
  measured, the rim averages RGB (75,48,36) against a core of (172,95,75), i.e. the
  colour falls toward black as alpha falls.  Resampling straight RGBA blends that
  darkness inward and rings the character with a black halo.

What consumes the output, which is NOT the usual importer:

* The frames are bound straight onto ``vendor_cheff_gatita.asset``'s ``idleSheets`` /
  ``walkSheets``, each cycle repeated into all EIGHT direction buckets — the art is a
  single front-facing view and ``DirectionalAnimator`` never flips.  Her four static
  directional poses must be CLEARED for that to render at all: ``BuildSet`` prefers
  ``directional`` over ``sheets``.
* Import settings need no authoring — ``ValkurAssetPostprocessor`` gives everything under
  ``Art/NPC/`` 64 PPU and a ``(0.5, 0)`` pivot, which is exactly the ground anchor this
  script cuts to.
* ``EntityAssetConfig.statePacing`` slows her idle to 0.40x (0.375 s/frame, a 2.25 s
  breath) while her walk stays at the default rate, tuned against her 0.8 u/s speed.
* She paces because ``assignments.json`` puts her on the ``NPC_Stroller`` FSM set by
  entity id, and ``patrolType: stroll`` gives her a 2.5-unit horizontal path centred on
  her stall.  ``GatitaAnimationDataTests`` pins all of it.

Run:  python tools/atlas/wave6/build_gatita_frames.py
"""

from __future__ import annotations

import json
from pathlib import Path

import numpy as np
from PIL import Image

REPO = Path(__file__).resolve().parents[3]
SOURCE = REPO / "unity/Valkur/Assets/_Project/Art/NPC/neutral/vendors/cheff/gatita_chanchita/new"
OUT_ROOT = REPO / "unity/Valkur/Assets/_Project/Art/NPC/neutral/vendors/cheff/gatita_chanchita"
MANIFEST = REPO / "tools/atlas/generated/gatita_frames_manifest.json"

# Which sheet becomes which state.
#
# idle_3 of the three idle sheets: it is the only one with no artefacts (idle_2
# carries a yellow glitch on the crown in frame 1 and a stray blob by the tail in
# frame 6), and its body height varies by only +-2.7% against idle_1's +-6%, which
# reads as breathing rather than as bobbing.
SHEETS = {
    "idle": "gatita_idle_3.png",
    "walk": "gatita_walking.png",
}

# Body height to normalise to, in pixels.
#
# The shipped Gatita is a 240 px body at 64 PPU with scaleIdle 0.3, i.e. 1.125 world
# units. Matching it means the new animation changes how she MOVES and nothing about
# how big she is — no MonsterDefinition edit, no re-tuning of her chat range.
TARGET_BODY_PX = 240
PIXELS_PER_UNIT = 64

# Alpha above which a pixel counts as part of the character.
ALPHA_FLOOR = 16

# A frame narrower than this is a speck, not a character. idle_2 has a 1x9 one.
MIN_FRAME_WIDTH = 40

# A row needs this fraction of the frame's widest row to count as "the body is here".
# Below it we are looking at a tail tip or a whisker.
EXTENT_FLOOR = 0.18

# Fraction of body height, measured up from the ground line, used to find the feet.
FOOT_BAND = 0.10

# Transparent margin around the canvas, so bilinear filtering and any atlas padding
# never sample a neighbouring frame.
CANVAS_PAD = 6


def load(path: Path) -> np.ndarray:
    return np.array(Image.open(path).convert("RGBA"))


def frame_columns(alpha: np.ndarray) -> list[tuple[int, int]]:
    """Contiguous runs of columns containing the character, specks dropped."""
    occupied = (alpha > ALPHA_FLOOR).any(axis=0)

    runs, start = [], None
    for x, on in enumerate(occupied):
        if on and start is None:
            start = x
        elif not on and start is not None:
            runs.append((start, x - 1))
            start = None
    if start is not None:
        runs.append((start, len(occupied) - 1))

    return [(a, b) for a, b in runs if b - a + 1 >= MIN_FRAME_WIDTH]


def body_rows(alpha: np.ndarray) -> tuple[int, int]:
    """(top, ground) rows of the body, ignoring slivers like a tail tip."""
    per_row = (alpha > ALPHA_FLOOR).sum(axis=1)
    widest = per_row.max()
    solid = np.where(per_row >= widest * EXTENT_FLOOR)[0]
    return int(solid[0]), int(solid[-1])


def foot_centre(alpha: np.ndarray, top: int, ground: int) -> float:
    """Horizontal centre of the pixels she is standing on."""
    band = max(1, int((ground - top + 1) * FOOT_BAND))
    slab = alpha[max(top, ground - band + 1): ground + 1] > ALPHA_FLOOR
    xs = np.where(slab.any(axis=0))[0]
    return float((xs[0] + xs[-1]) / 2.0) if len(xs) else alpha.shape[1] / 2.0


def resize_premultiplied(rgba: np.ndarray, size: tuple[int, int]) -> Image.Image:
    """Scale without bleeding the black of transparent pixels into the edges."""
    image = Image.fromarray(rgba, "RGBA")
    return image.convert("RGBa").resize(size, Image.LANCZOS).convert("RGBA")


def build_state(state: str, sheet_name: str) -> dict:
    sheet = load(SOURCE / sheet_name)
    alpha = sheet[:, :, 3]

    runs = frame_columns(alpha)
    if not runs:
        raise SystemExit(f"{sheet_name}: no frames found")

    # Measure every frame before cutting any, so the scale is a property of the
    # SHEET rather than of whichever frame happens to be processed first.
    measured = []
    for x0, x1 in runs:
        cell = sheet[:, x0:x1 + 1]
        top, ground = body_rows(cell[:, :, 3])
        measured.append(
            {
                "cell": cell,
                "top": top,
                "ground": ground,
                "height": ground - top + 1,
                "foot_x": foot_centre(cell[:, :, 3], top, ground),
            }
        )

    median_height = float(np.median([m["height"] for m in measured]))
    scale = TARGET_BODY_PX / median_height

    # The canvas has to hold the tallest frame and the widest reach either side of
    # the feet, so nothing is ever clipped by the frame that happens to lean most.
    canvas_h = int(round(max(m["height"] for m in measured) * scale)) + CANVAS_PAD * 2
    half_w = 0
    for m in measured:
        cell_alpha = m["cell"][:, :, 3]
        xs = np.where((cell_alpha > ALPHA_FLOOR).any(axis=0))[0]
        half_w = max(half_w, m["foot_x"] - xs[0], xs[-1] - m["foot_x"])
    canvas_w = int(round(half_w * 2 * scale)) + CANVAS_PAD * 2

    out_dir = OUT_ROOT / state
    out_dir.mkdir(parents=True, exist_ok=True)
    for stale in out_dir.glob("gatita_%s_*.png" % state):
        stale.unlink()

    written = []
    for index, m in enumerate(measured):
        # Crop to the body's vertical span; the horizontal span stays whole so the
        # tail is never cut off by its own frame.
        body = m["cell"][m["top"]: m["ground"] + 1]
        scaled_w = max(1, int(round(body.shape[1] * scale)))
        scaled_h = max(1, int(round(body.shape[0] * scale)))
        sprite = resize_premultiplied(body, (scaled_w, scaled_h))

        canvas = Image.new("RGBA", (canvas_w, canvas_h), (0, 0, 0, 0))

        # Feet on the canvas centre line, body standing on the padded bottom edge.
        # Both anchors are what make the frames sit still when played in sequence.
        left = int(round(canvas_w / 2.0 - m["foot_x"] * scale))
        top = canvas_h - CANVAS_PAD - scaled_h
        canvas.alpha_composite(sprite, (left, top))

        name = f"gatita_{state}_{index}.png"
        canvas.save(out_dir / name)
        written.append(name)

    return {
        "state": state,
        "source": sheet_name,
        "frames": written,
        "canvas": [canvas_w, canvas_h],
        "scale": round(scale, 5),
        "medianBodyPx": round(median_height, 1),
        "targetBodyPx": TARGET_BODY_PX,
        "pixelsPerUnit": PIXELS_PER_UNIT,
    }


def main() -> None:
    records = [build_state(state, sheet) for state, sheet in SHEETS.items()]

    MANIFEST.parent.mkdir(parents=True, exist_ok=True)
    MANIFEST.write_text(
        json.dumps({"character": "gatita", "states": records}, indent=2) + "\n",
        encoding="utf-8",
    )

    print(f"wrote {MANIFEST.relative_to(REPO)}")
    for r in records:
        print(
            "  %-5s %d frames  canvas %dx%d  scale %.3f (median body %.0f -> %d)"
            % (r["state"], len(r["frames"]), r["canvas"][0], r["canvas"][1],
               r["scale"], r["medianBodyPx"], r["targetBodyPx"])
        )


if __name__ == "__main__":
    main()
