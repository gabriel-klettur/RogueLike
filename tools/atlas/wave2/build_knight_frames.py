#!/usr/bin/env python3
"""Cut the knight_red character sheets into aligned, game-sized animation frames.

Why not slice_prop_sheet.py alone
---------------------------------
That tool trims every crop tight to its own alpha, which is exactly right for a
prop and exactly wrong for an animation: the knight's cape and sword change the
bounding box from frame to frame, so a tight-trimmed walk cycle jitters and the
feet leave the ground. What an animation needs is one canvas shared by every
frame of a state, with the character's ground line pinned to the same pixel.

It does reuse that tool's segmentation, via the ``*.slices.json`` it writes: the
per-object boxes are already correct, and cutting the frame out of the sheet by
grid cell alone would let a neighbouring frame's sword bleed in.

Alignment
---------
Every sheet is an even grid (asserted below: each detected object's centre falls
in its own cell). Two anchors put a frame on the shared canvas:

* ``anchor_x`` = the CELL's centre, not the body's. Anchoring on the body would
  cancel the very motion the animation is made of - a walk's hip sway, a slash's
  lunge - and leave the knight marching on the spot.
* ``ground_y`` = the lowest body pixel across the row, so the feet land on the
  same line in every frame. It is taken from each frame's LARGEST connected
  component: a torn-off cape tip sits below the boots in several frames and
  would drag the ground line down with it. A frame that genuinely leaves the
  ground (the jump kick) floats above the line, which is the point.

The row matters: a 4x2 sheet draws two independent ground lines, so the anchor
is computed per row.

Sizing
------
Frames land in ``Art/NPC/`` where ValkurAssetPostprocessor forces PPU 64 and a
bottom-centre pivot, so 128 px of art is 2 world units - the player's height.
The three source families were rendered at different sizes (the unarmed sheets
draw a visibly bigger knight), so each is scaled by its own factor, derived from
the TALLEST body in the sheet - the one frame where the knight is upright.
"""

from __future__ import annotations

import json
import os
import sys

import numpy as np
from PIL import Image
from scipy import ndimage

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
OUT_DIR = os.path.join(REPO, "unity", "Valkur", "Assets", "_Project",
                       "Art", "NPC", "monsters", "knight_red")

# The knight stands 112 px tall, inside frames that are whatever the widest pose
# needs. At PPU 64 that is 1.75 world units - a shade under the player's 2, which
# suits a humanoid enemy and leaves the sword room to swing without the frame
# growing past the sprite atlas's comfort zone.
TARGET_BODY_PX = 112
ALPHA_SOLID = 190     # same core threshold slice_prop_sheet segments on
ALPHA_KEEP = 16       # keep soft edges, drop the haze

# The six authored turnaround poses, in the order they are drawn, and the compass
# direction each one faces. The sheet draws six of the eight; northEast and east
# are produced by mirroring their western twins, which is how a 6-pose turnaround
# is meant to be completed. Any single pose is one Inspector drag to re-assign if
# a direction reads wrong in game.
TURNAROUND = {
    3: "south",
    4: "southWest",
    5: "west",
    6: "north",
    7: "northWest",
    8: "southEast",
}
MIRRORED_FROM = {"northEast": "northWest", "east": "west"}

# state -> (sheet stem, grid cols, grid rows)
STATES = {
    "walk":       ("knight_red_walk_side_sheet", 8, 1),
    "run":        ("knight_red_run_side_sheet", 8, 1),
    "slash":      ("knight_red_attack_slash_sheet", 8, 1),
    "shieldbash": ("knight_red_attack_shield_bash_sheet", 8, 1),
    "punch":      ("knight_plumed_punch_sheet", 4, 2),
    "kick":       ("knight_plumed_kick_sheet", 4, 2),
    "jumpkick":   ("knight_plumed_jump_kick_sheet", 4, 2),
}
IDLE_SHEET = "knight_red_idle_and_walk_sheet"


def body_box(patch: np.ndarray):
    """Bounding box of the largest solid component - the knight, not his debris."""
    solid = patch[..., 3] >= ALPHA_SOLID
    labels, n = ndimage.label(solid, structure=np.ones((3, 3)))
    if n == 0:
        return None
    biggest = int(np.bincount(labels.ravel())[1:].argmax()) + 1
    ys, xs = np.nonzero(labels == biggest)
    return xs.min(), ys.min(), xs.max() + 1, ys.max() + 1


def load_slices(slices_root: str, stem: str) -> dict:
    with open(os.path.join(slices_root, f"{stem}.slices.json"), encoding="utf-8") as fh:
        return json.load(fh)


def frame_patches(slices_root: str, stem: str, manifest: dict):
    """Each object's own isolated pixels, plus where they sit on the sheet."""
    crops_dir = os.path.join(slices_root, stem)
    out = []
    for item in manifest["items"]:
        patch = np.asarray(
            Image.open(os.path.join(crops_dir, item["file"])).convert("RGBA"))
        x0, y0, _, _ = item["sheet_box"]
        # sheet_box is the pre-trim box; the crop was trimmed tight inside it, so
        # recover the true offset from the trimmed size against the padded box.
        out.append({"index": item["index"], "patch": patch,
                    "sheet_box": item["sheet_box"], "x0": x0, "y0": y0})
    return out


def build_state(slices_root: str, stem: str, cols: int, rows: int):
    """Aligned frames for one animation state, at source resolution."""
    manifest = load_slices(slices_root, stem)
    sheet = np.asarray(Image.open(manifest["source"]).convert("RGBA"))
    sheet_h, sheet_w = sheet.shape[:2]
    cell_w, cell_h = sheet_w / cols, sheet_h / rows

    frames = []
    for item in manifest["items"]:
        x0, y0, x1, y1 = item["sheet_box"]
        cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
        col, row = int(cx // cell_w), int(cy // cell_h)
        if row * cols + col != item["index"]:
            raise SystemExit(f"{stem}#{item['index']} does not sit in its own grid cell "
                             f"(landed r{row}c{col}) — the even-grid assumption is wrong.")
        # The object's own pixels only, read back out of the sheet through its box,
        # so a neighbouring frame's sword never bleeds in.
        patch = sheet[y0:y1, x0:x1].copy()
        patch[..., 3] = np.where(patch[..., 3] < ALPHA_KEEP, 0, patch[..., 3])
        bb = body_box(patch)
        if bb is None:
            raise SystemExit(f"{stem}#{item['index']} has no solid body")
        frames.append({"index": item["index"], "row": row, "col": col, "patch": patch,
                       "x0": x0, "y0": y0, "body": bb})

    # One ground line per row; one anchor column per cell.
    ground = {}
    for f in frames:
        gy = f["y0"] + f["body"][3]
        ground[f["row"]] = max(ground.get(f["row"], 0), gy)
    for f in frames:
        f["anchor_x"] = (f["col"] + 0.5) * cell_w
        f["anchor_y"] = ground[f["row"]]

    # Canvas: the widest reach any frame needs from its anchor, in all four
    # directions, so every frame shares one geometry.
    left = max(int(round(f["anchor_x"] - f["x0"])) for f in frames)
    right = max(int(round(f["x0"] + f["patch"].shape[1] - f["anchor_x"])) for f in frames)
    up = max(f["anchor_y"] - f["y0"] for f in frames)
    down = max(f["y0"] + f["patch"].shape[0] - f["anchor_y"] for f in frames)

    canvas_w, canvas_h = left + right, up + down
    out = []
    for f in sorted(frames, key=lambda x: x["index"]):
        canvas = np.zeros((canvas_h, canvas_w, 4), dtype=np.uint8)
        px = int(round(f["x0"] - f["anchor_x"] + left))
        py = int(round(f["y0"] - f["anchor_y"] + up))
        ph, pw = f["patch"].shape[:2]
        canvas[py:py + ph, px:px + pw] = f["patch"]
        out.append(canvas)

    tallest_body = max(f["body"][3] - f["body"][1] for f in frames)
    return out, tallest_body


def resample(canvas: np.ndarray, scale: float) -> Image.Image:
    h, w = canvas.shape[:2]
    target = (max(1, round(w * scale)), max(1, round(h * scale)))
    # Premultiplied ('RGBa') so downscaling never averages the zeroed RGB of a
    # transparent pixel into the edges and rings the knight with a dark halo.
    img = Image.fromarray(canvas, "RGBA").convert("RGBa").resize(target, Image.LANCZOS).convert("RGBA")
    arr = np.array(img)
    arr[arr[..., 3] < 6] = 0
    return Image.fromarray(arr, "RGBA")


def build_idle(slices_root: str):
    """The six authored turnaround poses, on one shared canvas."""
    manifest = load_slices(slices_root, IDLE_SHEET)
    sheet = np.asarray(Image.open(manifest["source"]).convert("RGBA"))
    by_index = {it["index"]: it for it in manifest["items"]}

    poses = {}
    for index, direction in TURNAROUND.items():
        if index not in by_index:
            raise SystemExit(f"{IDLE_SHEET}: turnaround pose #{index} was not sliced")
        x0, y0, x1, y1 = by_index[index]["sheet_box"]
        patch = sheet[y0:y1, x0:x1].copy()
        patch[..., 3] = np.where(patch[..., 3] < ALPHA_KEEP, 0, patch[..., 3])
        bb = body_box(patch)
        if bb is None:
            raise SystemExit(f"{IDLE_SHEET}#{index} has no solid body")
        poses[direction] = {"patch": patch, "body": bb}

    # A static pose carries no motion to preserve, so anchor it on the body
    # itself: feet on the canvas floor, hips on the canvas centre line.
    left = max(int(round((p["body"][0] + p["body"][2]) / 2)) for p in poses.values())
    right = max(p["patch"].shape[1] - int(round((p["body"][0] + p["body"][2]) / 2))
                for p in poses.values())
    up = max(p["body"][3] for p in poses.values())
    down = max(p["patch"].shape[0] - p["body"][3] for p in poses.values())

    canvas_w, canvas_h = left + right, up + down
    out = {}
    for direction, p in poses.items():
        canvas = np.zeros((canvas_h, canvas_w, 4), dtype=np.uint8)
        px = left - int(round((p["body"][0] + p["body"][2]) / 2))
        py = up - p["body"][3]
        ph, pw = p["patch"].shape[:2]
        canvas[py:py + ph, px:px + pw] = p["patch"]
        out[direction] = canvas

    tallest = max(p["body"][3] - p["body"][1] for p in poses.values())
    return out, tallest


def main() -> int:
    if len(sys.argv) < 2:
        print("usage: build_knight_frames.py <slices-root>", file=sys.stderr)
        return 1
    slices_root = sys.argv[1]
    os.makedirs(OUT_DIR, exist_ok=True)

    written = []

    idle_canvases, idle_body = build_idle(slices_root)
    idle_scale = TARGET_BODY_PX / idle_body
    for direction, canvas in idle_canvases.items():
        img = resample(canvas, idle_scale)
        name = f"knight_red_idle_{direction.lower()}"
        img.save(os.path.join(OUT_DIR, f"{name}.png"))
        written.append((name, img.size))
    for direction, source in MIRRORED_FROM.items():
        img = resample(idle_canvases[source], idle_scale).transpose(Image.FLIP_LEFT_RIGHT)
        name = f"knight_red_idle_{direction.lower()}"
        img.save(os.path.join(OUT_DIR, f"{name}.png"))
        written.append((name, img.size))
    print(f"idle: 6 authored + {len(MIRRORED_FROM)} mirrored, body {idle_body}px "
          f"-> {TARGET_BODY_PX}px (x{idle_scale:.3f})")

    for state, (stem, cols, rows) in STATES.items():
        canvases, body = build_state(slices_root, stem, cols, rows)
        scale = TARGET_BODY_PX / body
        for i, canvas in enumerate(canvases):
            img = resample(canvas, scale)
            # The art faces left. A mirrored copy per frame is what lets the
            # east-facing directions read correctly: the animator never flips a
            # directional sprite (ChaseState says so in as many words), so the
            # mirror has to exist as its own sprite.
            name = f"knight_red_{state}_w{i}"
            img.save(os.path.join(OUT_DIR, f"{name}.png"))
            written.append((name, img.size))

            mirrored = img.transpose(Image.FLIP_LEFT_RIGHT)
            name_e = f"knight_red_{state}_e{i}"
            mirrored.save(os.path.join(OUT_DIR, f"{name_e}.png"))
            written.append((name_e, mirrored.size))
        print(f"{state:11s}: {len(canvases)} frames x2 (west + mirrored east), "
              f"body {body}px -> {TARGET_BODY_PX}px (x{scale:.3f}), "
              f"frame {resample(canvases[0], scale).size}")

    print(f"\n{len(written)} PNGs -> {os.path.relpath(OUT_DIR, REPO)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
