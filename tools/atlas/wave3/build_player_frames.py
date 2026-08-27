#!/usr/bin/env python3
"""Cut the side-view player sheets into aligned, mirrored, game-sized frames.

The three characters staged under ``staging/players/`` are
drawn **facing LEFT, one direction only** - the same way ``knight_red``'s sheets were,
and worth measuring rather than assuming: reading it backwards is invisible in every
still frame and produces a character that faces away from the cursor in game, because
``Direction.East`` is +X (``DirectionalAnimator.FrameLogic`` resolves 0 degrees to East)
so the east buckets must hold the RIGHT-facing copy. Valkur's ``DirectionalAnimator`` never
flips a sprite — ``ChaseState`` says so in as many words, and ``PlayerController``
only touches ``flipX`` when there is no animator at all — so the left-facing half
of a 2-direction rig has to exist as its own sprite. This tool bakes it.

Relationship to the other two tools
-----------------------------------
* ``slice_prop_sheet.py`` owns the segmentation. Run it first; this tool reads the
  ``*.slices.json`` it writes. Cutting a frame out of the sheet by grid cell alone
  would let a neighbouring frame's axe bleed into it.
* ``wave2/build_knight_frames.py`` is this tool's direct ancestor and still owns
  ``knight_red`` (a monster, six authored turnaround poses, art facing LEFT). The
  differences that made a copy cheaper than a parameter: the grid here is inferred
  per sheet rather than declared, the source art faces RIGHT, the output is a
  player rather than a monster, and the manifest carries an eight-bucket direction
  layout that the monster manifest expresses per-direction instead.

Do NOT reach for ``slice_prop_sheet.py``'s crops directly. It trims every crop
tight to its own alpha, which is right for a prop and wrong for a cycle: the cape
and the axe move the bounding box every frame, so a tight-trimmed walk jitters and
the feet leave the ground.

Alignment
---------
Two anchors put each frame on one canvas shared by every frame of the state:

* ``anchor_x`` = the CELL's centre, never the body's. Anchoring on the body would
  cancel the very motion the animation is made of — a walk's hip sway, a slash's
  lunge — and leave the character marching on the spot.
* ``anchor_y`` = the lowest body pixel across the row, so the feet land on the same
  line in every frame. Taken from each frame's LARGEST connected component,
  because a trailing cape tip or a dropped weapon sits below the boots in some
  frames and would drag the ground line down with it. A frame that genuinely
  leaves the ground (the elf's jump attack) floats above the line, which is the
  point.

The row matters: a 4x2 sheet draws two independent ground lines, so the ground is
computed per row.

Grid inference
--------------
The knight tool declared ``(cols, rows)`` per sheet. Here it is inferred: item
centres are clustered into rows by their vertical gaps, and the cell index comes
from each item's position in ``slice_prop_sheet``'s reading order — NOT from where
its centre happens to land. That distinction matters for exactly one shipped
sheet: ``knight_unarmed_death_7f``, where the knight falls and slides a full
half-cell to the left, so a position-derived cell index puts two frames in one
cell and leaves another empty. Deriving from reading order keeps the slide as
motion, which is what a death animation is. A frame whose centre escapes its own
cell is reported as a warning, since on a walk cycle it would mean the row
clustering picked the wrong grid.

Sizing
------
Frames land in ``Art/Characters/`` where ``ValkurAssetPostprocessor`` forces
PPU 64 and a bottom-centre pivot. The five characters already in the game stand
115 px tall at their tallest frame, so ``TARGET_BODY_PX`` matches that exactly:
a swapped-in character keeps the same world height, and every melee range,
projectile spawn offset and camera lead tuned against the old art still reads.
Each state is scaled by its own factor derived from the TALLEST body in the
sheet — the one frame where the character stands upright — because the source
families were rendered at visibly different sizes.

Usage
-----
    python slice_prop_sheet.py --all --sheet-dir <staging/players/knight> --out <slices>
    python slice_prop_sheet.py --all --sheet-dir <staging/players/barbarian> --out <slices>
    python slice_prop_sheet.py --all --sheet-dir <staging/players/elf> --out <slices>
    python wave3/build_player_frames.py <slices> [--dry-run]
"""

from __future__ import annotations

import argparse
import json
import os
import re
import sys

import numpy as np
from PIL import Image
from scipy import ndimage

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
ART_ROOT = os.path.join(REPO, "unity", "Valkur", "Assets", "_Project", "Art", "Characters")
MANIFEST_PATH = os.path.join(REPO, "tools", "atlas", "generated",
                             "player_frames_manifest_wave3.json")
ART_ROOT_UNITY = "Assets/_Project/Art/Characters"

# The five characters already in the game stand 115 px at their tallest frame
# (measured across all 40 frames of barbarian_idle.png). Matching it keeps every
# range and offset tuned against the old art valid.
TARGET_BODY_PX = 115
ALPHA_SOLID = 190     # the core threshold slice_prop_sheet segments on
ALPHA_KEEP = 16       # keep soft edges, drop the haze

# S, SE, E, NE, N, NW, W, SW -- the order BuildEightDirectionalSet slices, and the
# order the manifest's sprite lists must be written in.
DIRECTIONS = ["south", "southEast", "east", "northEast",
              "north", "northWest", "west", "southWest"]

# The authored art faces WEST, so the west half of the rig is the authored copy and
# the east half is its mirror - hence the `w`/`e` suffixes rather than a
# left/right pair, matching wave2/build_knight_frames.py.
#
# South and north are ambiguous in a 2-direction rig. Both take the EAST copy, so a
# player walking or aiming straight up and straight down keeps one consistent
# silhouette instead of flipping as the cursor crosses the vertical.
SOURCE_FACING = "w"
MIRRORED_FACING = "e"
BUCKET_FACING = {
    "south": "e", "southEast": "e", "east": "e", "northEast": "e", "north": "e",
    "northWest": "w", "west": "w", "southWest": "w",
}

# Multiplies the automatic scale for one staged sheet. The AI that drew these
# rendered every sheet at its own zoom, and the median-height reference below can
# only normalise a sheet whose character stands upright in at least half its
# frames. Where it does not -- a swing that is crouched or lunging from the first
# frame to the last -- the automatic answer is wrong by exactly the amount the
# pose is compressed, and no statistic recovers it from the pixels. So it is
# declared, the way tools/atlas/wave2/classify.py declares its prop table rather
# than guessing. 1.0 (absent) means the automatic reference was right.
#
# Calibrate against the character's IDLE sheet: place the two side by side and
# match the head, not the bounding box.
SCALE_OVERRIDE: dict[str, float] = {
    # This sheet opens ALREADY AIRBORNE -- frame 0 is the crouch-and-leap, not a
    # stance -- so its foot line sits above the ground the rest of the row lands
    # on and the frame-0 reference under-measures the elf by 14.8%. Every other
    # state of all three characters lands within 3% of its own idle without help.
    "elf_attack_jump_8f": 0.871,
}


# ── What each player ships ────────────────────────────────────────────────────
#
# `states` maps an EntityAssetConfig slot to the staged sheet that fills it.
# `variants` are extra attacks, exposed through EntityAssetConfig.attackVariants
# rather than new AnimState values -- a new enum value missing from
# PlayerController.Movement's revert whitelist is entered and never left, while a
# variant INDEX under the existing Attack state inherits both whitelists for free.
# Index 0 is what a picker falls back to, so the default swing goes first.
#
# `staged` names the sheets deliberately NOT shipped, with the reason, so the next
# person does not have to re-derive it from the art.
PLAYERS = {
    # knight -> dwarf. Replaced wholesale by the wave4 set (unity/downloads/assets/dwaft).
    #
    # The UNARMED loadout ships, and it is the only one that can: the fourteen unarmed
    # sheets fill every slot with no gaps, while the five `dwarf_armed_*` sheets cover only
    # locomotion and one attack -- there is no armed hurt, death or cast. Shipping any of
    # them beside the rest would pop the sword and shield in and out of the character's
    # hands the moment he is hit, dies or casts. `dwarf_armed_equipment_daw` is the
    # unarmed-to-armed transition, so the art clearly anticipates an equip system; there
    # isn't one, and inventing it is not an art import's job. The armed five are staged
    # under staging/players/knight_wave4_armed/ waiting for it.
    "dwarf": {
        "source": "knight",
        "states": {
            "idle":    "knight_idle",
            "walk":    "knight_walking",
            "chase":   "knight_running",
            "cast":    "knight_spellcasting_1",
            "attack":  "knight_punch",
            "damage":  "knight_hit_reaction",
            "death":   "knight_die",
            "recover": "knight_knockdown_recovery",
        },
        # Rotated per swing by PlayerController.NextVariant; index 0 is the fallback.
        # charging_sprint is a shoulder-first lunge, not a run -- knight_running is the
        # locomotion cycle -- so it belongs here rather than in `chase`.
        "variants": [
            ("punch",  "knight_punch"),
            ("kick",   "knight_kick"),
            ("charge", "knight_charging_sprint"),
        ],
        # Five casting animations, rotated per cast. spellcasting_1 doubles as the base
        # `cast` slot so the character still casts with real art if the variants are lost.
        "cast_variants": [
            ("spell_1", "knight_spellcasting_1"),
            ("spell_2", "knight_spellcasting_2"),
            ("spell_3", "knight_spellcasting_3"),
            ("spell_4", "knight_spellcasting_4"),
            ("spell_5", "knight_spellcasting_5"),
        ],
        "staged": {
            "knight_wave4_armed/*": "the sword-and-shield loadout -- idle, walking, "
                                    "running, one attack and the draw transition. A second "
                                    "loadout for a future equip system, not a second "
                                    "character, and unusable on its own because it has no "
                                    "hurt, death or cast",
            "wave3 knight_*_8f sheets": "the previous dwarf set, superseded wholesale by "
                                        "this one; kept in staging/players/knight/ as the "
                                        "record of what the character looked like before",
        },
    },
    # barbarian -> barbarian. The axe loadout ships whole. `cast` is deliberately
    # left EMPTY so EntityAnimationBinder falls it back to walk: the only cast art
    # is unarmed, and dropping the axe for the duration of a spell reads as a bug,
    # where walking through the cast merely reads as bland.
    "barbarian": {
        "source": "barbarian",
        "states": {
            "idle":   "barbarian_axe_idle_6f",
            "walk":   "barbarian_axe_walk_8f",
            "chase":  "barbarian_axe_run_8f",
            "attack": "barbarian_axe_attack_overhead_8f",
        },
        "variants": [
            ("overhead", "barbarian_axe_attack_overhead_8f"),
            ("swing",    "barbarian_axe_attack_swing_8f"),
        ],
        "staged": {
            "barbarian_unarmed_*": "a second loadout; shipping any of it beside the "
                                   "axe pops the weapon out of frame",
            "damage/death": "NO SOURCE ART EXISTS in either loadout -- both fall back "
                            "to idle. GrayscaleDeath still greys the corpse, so death "
                            "reads, but the pose does not sell it",
        },
    },
    # elf -> elven. Replaced wholesale by the wave4 set (unity/downloads/assets), which
    # is the only character where every slot has purpose-drawn art AND there is art left
    # over: three punches, three spellcasts and a rise from the floor.
    "elven": {
        "source": "elf",
        "states": {
            "idle":    "elf_idle",
            "walk":    "elf_walking",
            "chase":   "elf_run",
            "cast":    "elf_spellcasting_3",
            "attack":  "elf_punch",
            "damage":  "elf_hit_reaction",
            "death":   "elf_die",
            # The eighth state. DeathSequenceController.ReviveRoutine plays it once the
            # body is solid again and the corpse is gone.
            "recover": "elf_knockdown_recovery",
        },
        # Rotated per swing by PlayerController.NextVariant. Index 0 is the fallback, so
        # the plain punch goes first and the two showier moves follow.
        "variants": [
            ("punch",     "elf_punch"),
            ("kick",      "elf_kick_1"),
            ("run_punch", "elf_run_punch"),
        ],
        # Rotated per cast the same way. spellcasting_3 doubles as the base `cast` slot so
        # a character that somehow loses its variants still casts with real art.
        "cast_variants": [
            ("spell_3", "elf_spellcasting_3"),
            ("spell_1", "elf_spellcasting_1"),
            ("spell_2", "elf_spellcasting_2"),
        ],
        "staged": {
            "wave3 elf_* sheets": "the previous elven set, superseded wholesale by this "
                                  "one; kept in staging/players/elf/ as the record of what "
                                  "the character looked like before",
        },
    },
}


# ── Geometry ──────────────────────────────────────────────────────────────────

# A row of the body counts as "standing on something" once it is at least this
# fraction of the body's widest row. Boots seen from the side are a broad shape;
# an axe blade sweeping through the floor, a trailing cape tip and a thrown-back
# leg are all slivers. See foot_line() for why the distinction decides the anchor.
FOOT_WIDTH_FRACTION = 0.15


def body_mask(patch: np.ndarray):
    """The largest solid component -- the body, not the debris beside it."""
    solid = patch[..., 3] >= ALPHA_SOLID
    labels, n = ndimage.label(solid, structure=np.ones((3, 3)))
    if n == 0:
        return None
    biggest = int(np.bincount(labels.ravel())[1:].argmax()) + 1
    return labels == biggest


def body_box(patch: np.ndarray):
    """Bounding box of the largest solid component -- the body, not its debris."""
    mask = body_mask(patch)
    if mask is None:
        return None
    ys, xs = np.nonzero(mask)
    return xs.min(), ys.min(), xs.max() + 1, ys.max() + 1


def foot_line(patch: np.ndarray):
    """The row the character is STANDING on, which is not its lowest pixel.

    The weapon is held, so it belongs to the same connected component as the
    body: on a downward swing the axe head reaches the floor and the body's
    bounding box bottom follows it several dozen pixels past the boots. Anchoring
    the state on that made the whole character float for the rest of the swing --
    visible as the barbarian's overhead attack lifting off the ground halfway
    through, and the elf's jump attack never coming back down.

    So the anchor is the lowest row with real horizontal EXTENT, not the lowest
    row with any pixel at all. A pair of boots is wide; a blade edge, a cape tip
    and an outstretched leg are not. A frame that is genuinely airborne has no
    wide row down there either, so it anchors high and floats -- which is the
    point of a jump.
    """
    mask = body_mask(patch)
    if mask is None:
        return None
    widths = mask.sum(axis=1)
    threshold = max(3.0, widths.max() * FOOT_WIDTH_FRACTION)
    rows = np.nonzero(widths >= threshold)[0]
    if rows.size == 0:
        return None
    return int(rows.max()) + 1


def infer_grid(items, sheet_h):
    """(rows, cols) from the vertical gaps between item centres.

    Returns None when the rows come out ragged, which means the sheet is not the
    even grid every alignment step below assumes.
    """
    heights = sorted(b["sheet_box"][3] - b["sheet_box"][1] for b in items)
    median_h = heights[len(heights) // 2]

    # Key on the centre alone -- a tuple sort would fall through to comparing the
    # item dicts whenever two centres tie, which they do on any even row.
    centred = sorted(items, key=lambda b: (b["sheet_box"][1] + b["sheet_box"][3]) / 2)
    def cy(b):
        return (b["sheet_box"][1] + b["sheet_box"][3]) / 2

    rows = [[centred[0]]]
    for item in centred[1:]:
        if cy(item) - cy(rows[-1][-1]) > median_h * 0.6:
            rows.append([item])
        else:
            rows[-1].append(item)

    counts = {len(r) for r in rows}
    if len(counts) != 1:
        return None
    cols = counts.pop()
    if len(rows) * cols != len(items):
        return None
    return len(rows), cols


def build_state(slices_root: str, stem: str):
    """Aligned frames for one animation state, at source resolution."""
    with open(os.path.join(slices_root, f"{stem}.slices.json"), encoding="utf-8") as fh:
        manifest = json.load(fh)

    sheet = np.asarray(Image.open(manifest["source"]).convert("RGBA"))
    sheet_h, sheet_w = sheet.shape[:2]
    items = manifest["items"]

    grid = infer_grid(items, sheet_h)
    if grid is None:
        raise SystemExit(f"{stem}: rows came out ragged -- not an even grid")
    rows, cols = grid
    cell_w, cell_h = sheet_w / cols, sheet_h / rows

    frames = []
    for item in items:
        x0, y0, x1, y1 = item["sheet_box"]
        # The cell comes from slice_prop_sheet's reading order, not from where the
        # centre lands: a death animation slides its body out of its own cell on
        # purpose, and that translation is the animation.
        index = item["index"]
        row, col = divmod(index, cols)

        cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
        landed_col, landed_row = int(cx // cell_w), int(cy // cell_h)
        if (landed_row, landed_col) != (row, col):
            print(f"  note: {stem}#{index} sits in cell r{landed_row}c{landed_col}, "
                  f"not its own r{row}c{col} -- the pose translates that far")

        # The object's own pixels only, read back out of the sheet through its box,
        # so a neighbouring frame's weapon never bleeds in.
        patch = sheet[y0:y1, x0:x1].copy()
        patch[..., 3] = np.where(patch[..., 3] < ALPHA_KEEP, 0, patch[..., 3])
        bb = body_box(patch)
        feet = foot_line(patch)
        if bb is None or feet is None:
            raise SystemExit(f"{stem}#{index} has no solid body")
        frames.append({"index": index, "row": row, "col": col, "patch": patch,
                       "x0": x0, "y0": y0, "body": bb, "feet": feet})

    # One ground line per row; one anchor column per cell.
    ground = {}
    for f in frames:
        ground[f["row"]] = max(ground.get(f["row"], 0), f["y0"] + f["feet"])
    for f in frames:
        f["anchor_x"] = (f["col"] + 0.5) * cell_w
        f["anchor_y"] = ground[f["row"]]

    # Canvas: the widest reach any frame needs from its anchor, so every frame of
    # the state shares one geometry.
    left = max(int(round(f["anchor_x"] - f["x0"])) for f in frames)
    right = max(int(round(f["x0"] + f["patch"].shape[1] - f["anchor_x"])) for f in frames)
    up = max(f["anchor_y"] - f["y0"] for f in frames)

    # Nothing is reserved BELOW the ground line, so the canvas bottom IS the ground
    # line. ValkurAssetPostprocessor forces a (0.5, 0) pivot on everything under
    # Art/Characters/, and a pivot only lands on the feet if the feet are the
    # bottom row; reserving space under them would float the whole character by
    # that many pixels, silently and per state. What that clips is the handful of
    # pixels a cape tip or a trailing weapon draws below the boot line -- which in
    # a top-down view is drawn into the floor anyway. It is reported because a
    # large number there would mean the ground line, not the debris, is wrong.
    overhang = max(0, max(f["y0"] + f["patch"].shape[0] - f["anchor_y"] for f in frames))

    canvas_w, canvas_h = left + right, up
    out = []
    for f in sorted(frames, key=lambda x: x["index"]):
        canvas = np.zeros((canvas_h, canvas_w, 4), dtype=np.uint8)
        px = int(round(f["x0"] - f["anchor_x"] + left))
        py = int(round(f["y0"] - f["anchor_y"] + up))
        ph, pw = f["patch"].shape[:2]
        # Clip the paste to the canvas on every side: py + ph runs past the bottom
        # by `overhang`, and a pose that lunges can run past left/right too.
        sy0, sx0 = max(0, -py), max(0, -px)
        sy1, sx1 = min(ph, canvas_h - py), min(pw, canvas_w - px)
        if sy1 > sy0 and sx1 > sx0:
            canvas[py + sy0:py + sy1, px + sx0:px + sx1] = f["patch"][sy0:sy1, sx0:sx1]
        out.append(canvas)

    # The scale reference is FRAME 0's foot-to-crown height. Every sheet in this
    # wave opens on a neutral standing pose, and that is the only frame whose
    # height means "how big is this character", because the AI rendered each sheet
    # at its own zoom and every later frame is compressed or extended by its pose.
    #
    # The two statistics that look more robust both fail, in opposite directions,
    # and measurably: the tallest box is weapon-inclusive (the axe raised overhead
    # shares a connected component with the hands holding it), so it normalised
    # the barbarian's overhead swing to a 59px character against a 115px idle;
    # the median is dominated by whatever the sheet spends most of its frames
    # doing, so on a death -- four of seven frames lying down -- it took the height
    # of a PRONE body as the standing reference and rendered the knight at 405x263.
    # Frame 0 is upright in both.
    #
    # A sheet that opens mid-pose needs a SCALE_OVERRIDE; that is what it is for.
    first = min(frames, key=lambda f: f["index"])
    reference_body = first["feet"] - first["body"][1]
    return out, reference_body, (rows, cols), overhang


def resample(canvas: np.ndarray, scale: float) -> Image.Image:
    h, w = canvas.shape[:2]
    target = (max(1, round(w * scale)), max(1, round(h * scale)))
    # Premultiplied ('RGBa') so downscaling never averages the zeroed RGB of a
    # transparent pixel into the edges and rings the character with a dark halo.
    img = (Image.fromarray(canvas, "RGBA").convert("RGBa")
           .resize(target, Image.LANCZOS).convert("RGBA"))
    arr = np.array(img)
    arr[arr[..., 3] < 6] = 0
    return Image.fromarray(arr, "RGBA")


# ── Driver ────────────────────────────────────────────────────────────────────

def sheets_for(player: dict) -> dict:
    """Every distinct sheet stem this player needs, state slots and variants alike."""
    stems = dict(player["states"])
    for key, stem in player.get("variants", []):
        stems[f"variant:{key}"] = stem
    for key, stem in player.get("cast_variants", []):
        stems[f"cast:{key}"] = stem
    return stems


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("slices_root", help="Directory slice_prop_sheet.py wrote its "
                                        "*.slices.json and crops into")
    ap.add_argument("--dry-run", action="store_true",
                    help="Report what would be written without touching any file")
    args = ap.parse_args()

    manifest = {
        "generator": "tools/atlas/wave3/build_player_frames.py",
        "generatedFrom": os.path.basename(args.slices_root.rstrip("/\\")),
        "targetBodyPx": TARGET_BODY_PX,
        "players": [],
    }
    total_pngs = 0

    for player_key, player in PLAYERS.items():
        print(f"\n=== {player_key}  (from staging/players/{player['source']}/) ===")
        out_dir = os.path.join(ART_ROOT, player_key)
        if not args.dry_run:
            os.makedirs(out_dir, exist_ok=True)

        # One sheet can fill a state slot AND a variant (the default attack does
        # both), so build each distinct stem once and reuse the written sprites.
        built: dict[str, list[str]] = {}
        entry = {"playerKey": player_key, "states": [],
                 "attackVariants": [], "castVariants": []}

        for slot, stem in sheets_for(player).items():
            if stem in built:
                continue
            canvases, body, (rows, cols), overhang = build_state(args.slices_root, stem)
            scale = (TARGET_BODY_PX / body) * SCALE_OVERRIDE.get(stem, 1.0)
            # Drop the source character prefix and the staging suffixes: the
            # staged name carries a frame count (`_8f`) and sometimes an alternate
            # take marker (`_v2`) that identify a FILE IN downloads/, not a
            # shipped animation state.
            state_name = stem.split("_", 1)[1] if "_" in stem else stem
            state_name = re.sub(r"_\d+f(_v\d+)?$", "", state_name)

            names = []
            for i, canvas in enumerate(canvases):
                img = resample(canvas, scale)
                for facing, image in ((SOURCE_FACING, img),
                                      (MIRRORED_FACING, img.transpose(Image.FLIP_LEFT_RIGHT))):
                    name = f"{player_key}_{state_name}_{facing}{i}"
                    if not args.dry_run:
                        image.save(os.path.join(out_dir, f"{name}.png"))
                    total_pngs += 1
                names.append(f"{player_key}_{state_name}")
            built[stem] = [f"{player_key}_{state_name}_{{facing}}{i}"
                           for i in range(len(canvases))]
            clipped = round(overhang * scale)
            print(f"  {stem:38s} {cols}x{rows} grid, {len(canvases)} frames x2 "
                  f"(authored west + mirrored east), body {body}px -> "
                  f"{round(body * scale)}px (x{scale:.3f}), frame "
                  f"{resample(canvases[0], scale).size}"
                  + (f", clipped {clipped}px below the ground line" if clipped else ""))

        def bucket_list(stem: str) -> list[str]:
            """framesPerDirection * 8 sprite names, in S,SE,E,NE,N,NW,W,SW order."""
            templates = built[stem]
            out = []
            for direction in DIRECTIONS:
                facing = BUCKET_FACING[direction]
                for tpl in templates:
                    out.append(f"{ART_ROOT_UNITY}/{player_key}/"
                               f"{tpl.format(facing=facing)}.png")
            return out

        for slot, stem in player["states"].items():
            entry["states"].append({
                "state": slot,
                "framesPerDirection": len(built[stem]),
                "sprites": bucket_list(stem),
            })
        for key, stem in player.get("variants", []):
            entry["attackVariants"].append({
                "key": key,
                "framesPerDirection": len(built[stem]),
                "sprites": bucket_list(stem),
            })

        for key, stem in player.get("cast_variants", []):
            entry["castVariants"].append({
                "key": key,
                "framesPerDirection": len(built[stem]),
                "sprites": bucket_list(stem),
            })

        entry["stagedNotShipped"] = player["staged"]
        manifest["players"].append(entry)

    if not args.dry_run:
        os.makedirs(os.path.dirname(MANIFEST_PATH), exist_ok=True)
        with open(MANIFEST_PATH, "w", encoding="utf-8") as fh:
            json.dump(manifest, fh, indent=2)
            fh.write("\n")

    verb = "would write" if args.dry_run else "wrote"
    print(f"\n{verb} {total_pngs} PNGs under {os.path.relpath(ART_ROOT, REPO)}")
    print(f"{verb} manifest {os.path.relpath(MANIFEST_PATH, REPO)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
