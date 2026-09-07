#!/usr/bin/env python3
"""Cut a monster's character sheets into aligned, game-sized animation frames.

Generalises ``tools/atlas/wave2/build_knight_frames.py`` (built for exactly one
monster) into a manifest-driven tool, the same shape as the building-prop
pipeline:

    slice_prop_sheet.py     sheet PNG          -> crops + <sheet>.slices.json
    build_monster_frames.py crops + --config    -> Art/NPC/monsters/<key>/*.png
                                                    + monster_frames_manifest*.json
    MonsterFramesImporter   manifest            -> MonsterDefinition assets
                                                    + MonsterCatalog

Why not slice_prop_sheet.py alone
----------------------------------
That tool trims every crop tight to its own alpha, which is exactly right for a
prop and exactly wrong for an animation cycle: a cape or a sword changes the
bounding box from frame to frame, so a tight-trimmed walk cycle jitters and the
feet leave the ground. What an animation needs is one canvas shared by every
frame of a state, with the character's ground line pinned to the same pixel.

It does reuse that tool's segmentation via the ``*.slices.json`` it writes: the
per-frame boxes are already correct, and cutting a frame out of the sheet by
grid cell alone would let a neighbouring frame's sword bleed in.

Alignment (unchanged from the knight script)
---------------------------------------------
Every cyclic sheet is an even grid (asserted below: each frame's centre falls
in its own cell). Two anchors put a frame on the shared canvas:

* ``anchor_x`` = the CELL's centre, not the body's. Anchoring on the body would
  cancel the motion the animation is made of - a walk's hip sway, a slash's
  lunge - and leave the character marching on the spot.
* ``anchor_y`` = the lowest body pixel across the row (the ground line), taken
  from each frame's LARGEST connected component so a torn-off cape tip below
  the boots cannot drag the line down. A frame that genuinely leaves the
  ground floats above the line, which is the point.

A turnaround idle (several distinct poses, one per direction) anchors on the
body instead, because a static pose carries no motion to preserve: feet on the
canvas floor, hips on the canvas centre line.

Mirrors
-------
DirectionalAnimator never flips a sprite (see CLAUDE.md and
DirectionalAnimator.SpriteSetBuilder.BuildEightDirectionalSet) - a side-view
cycle authored facing one way needs its opposite-facing frames baked as real
PNGs, not produced at runtime. This tool bakes them in Python
(``Image.transpose(FLIP_LEFT_RIGHT)``), the same choice the knight script
made and the lower-risk of the two options considered for this generalisation
(see the "mirror" design note in the module docstring of
MonsterFramesImporter.cs and in .github/skills/asset-pipeline/SKILL.md).

Direction bucket assignment
----------------------------
A 2-directional source (west + mirrored east) still has to fill all eight
compass buckets DirectionalAnimator understands. The default assignment below
is exactly what ``knight_red`` ships with today (pinned by
``KnightRedSpriteIntegrityTests.SideByDirection``): south/north/northWest/
west/southWest draw from the west pose, southEast/east/northEast draw from the
mirrored east pose. A monster's config may override ``directionMap`` if its
source art suits a different split.

Config schema (--config)
-------------------------
::

    {
      "monsters": {
        "<monsterKey>": {
          "displayName": "...",              # optional, default = title-cased key
          "slicesRoot": "unity/downloads/...",  # where slice_prop_sheet.py wrote its output
          "outDir": "...",                   # optional; default Art/NPC/monsters/<key>/
          "targetBodyPx": 112,               # optional, default DEFAULT_TARGET_BODY_PX
          "idle": {                          # optional: an authored turnaround
            "sheet": "<slices .slices.json stem>",
            "poses": {"3": "south", "4": "southWest", ...},   # raw slice index -> direction
            "mirrorFrom": {"east": "west"}   # optional: fill remaining directions by mirroring
          },
          "states": {                        # zero or more cyclic (multi-frame) states
            "walk": {"sheet": "<stem>", "cols": 8, "rows": 1, "mirror": true},
            "cast": {"sheet": "<stem>", "cols": 8, "rows": 1, "mirror": true}
          },
          "directionMap": {                  # optional override of the default bucket->side map
            "south": "west", "southEast": "east", ...
          }
        }
      }
    }

Usage
-----
    python build_monster_frames.py --config <json> --manifest <out.json>
    python build_monster_frames.py --config <json> --manifest <out.json> --allow-outside-assets
"""

from __future__ import annotations

import argparse
import json
import math
import os
import sys
from typing import Dict, List, Optional, Tuple

import numpy as np
from PIL import Image
from scipy import ndimage

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
DEFAULT_OUT_ROOT = os.path.join(
    REPO, "unity", "Valkur", "Assets", "_Project", "Art", "NPC", "monsters")
DEFAULT_MANIFEST = os.path.join(
    REPO, "tools", "atlas", "generated", "monster_frames_manifest.json")
ASSETS_PREFIX = "unity/Valkur/Assets/"

ALPHA_SOLID = 190     # same core threshold slice_prop_sheet segments on
ALPHA_KEEP = 16        # keep soft edges, drop the haze
DEFAULT_TARGET_BODY_PX = 112

# The animator's bucket order (DirectionalAnimator.SpriteSetBuilder / DirectionalSprites).
DIRECTIONS = ["south", "southEast", "east", "northEast",
              "north", "northWest", "west", "southWest"]
VALID_DIRECTIONS = set(DIRECTIONS)

# Which side of a 2-directional (west + mirrored east) cycle each bucket draws
# from, by default. This is exactly what knight_red ships with -
# KnightRedSpriteIntegrityTests.SideByDirection = {w,e,e,e,w,w,w,w} in this
# same S,SE,E,NE,N,NW,W,SW order.
DEFAULT_DIRECTION_MAP = {
    "south": "west", "southEast": "east", "east": "east", "northEast": "east",
    "north": "west", "northWest": "west", "west": "west", "southWest": "west",
}

NAME_ALLOWED = set("abcdefghijklmnopqrstuvwxyz0123456789_")


# --------------------------------------------------------------------------
# Shared geometry (ported from build_knight_frames.py, generalised)
# --------------------------------------------------------------------------

def body_box(patch: np.ndarray) -> Optional[Tuple[int, int, int, int]]:
    """Bounding box of the largest solid component - the body, not its debris."""
    solid = patch[..., 3] >= ALPHA_SOLID
    labels, n = ndimage.label(solid, structure=np.ones((3, 3)))
    if n == 0:
        return None
    biggest = int(np.bincount(labels.ravel())[1:].argmax()) + 1
    ys, xs = np.nonzero(labels == biggest)
    return xs.min(), ys.min(), xs.max() + 1, ys.max() + 1


def load_slices(slices_root: str, stem: str) -> dict:
    path = os.path.join(slices_root, f"{stem}.slices.json")
    if not os.path.exists(path):
        raise SystemExit(f"missing {path} - run slice_prop_sheet.py on this sheet first")
    with open(path, encoding="utf-8") as fh:
        return json.load(fh)


def resample(canvas: np.ndarray, scale: float) -> Image.Image:
    h, w = canvas.shape[:2]
    target = (max(1, round(w * scale)), max(1, round(h * scale)))
    # Premultiplied ('RGBa') so downscaling never averages the zeroed RGB of a
    # transparent pixel into the edges and rings the sprite with a dark halo.
    img = Image.fromarray(canvas, "RGBA").convert("RGBa").resize(target, Image.LANCZOS).convert("RGBA")
    arr = np.array(img)
    arr[arr[..., 3] < 6] = 0
    return Image.fromarray(arr, "RGBA")


def build_state_canvases(slices_root: str, tag: str, stem: str, cols: int, rows: int):
    """Aligned frames for one cyclic animation state, at source resolution.

    Returns ``(canvases_in_index_order, tallest_body_px)``.
    """
    manifest = load_slices(slices_root, stem)
    sheet = np.asarray(Image.open(manifest["source"]).convert("RGBA"))
    sheet_h, sheet_w = sheet.shape[:2]
    cell_w, cell_h = sheet_w / cols, sheet_h / rows

    # A slicer may hand over the anchor and the row it MEASURED, instead of leaving them
    # to be derived from an even grid. Some sheets simply are not on one: on the wave13
    # monsters the figures are wider than their nominal cell and every one overlaps its
    # neighbours', so 148 components straddle a cut line and the assertion below is not a
    # safety check but a false alarm. When the slicer supplies `anchor_x` it has fitted an
    # evenly spaced ladder through the measured figure centres, which is the same quantity
    # the cell centre stands in for -- see slice_wave13_sheets.py. Slices without these
    # fields take the original grid path unchanged.
    explicit = all("anchor_x" in item for item in manifest["items"])

    frames = []
    for item in manifest["items"]:
        x0, y0, x1, y1 = item["sheet_box"]
        cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
        if explicit:
            row = int(item.get("row", 0))
            col = None
        else:
            col, row = int(cx // cell_w), int(cy // cell_h)
            if row * cols + col != item["index"]:
                raise SystemExit(f"{tag}/{stem}#{item['index']} does not sit in its own grid cell "
                                 f"(landed r{row}c{col}) - the even-grid assumption is wrong.")
        # The frame's own pixels only, read back out of the sheet through its box,
        # so a neighbouring frame's sword/cape never bleeds in.
        patch = sheet[y0:y1, x0:x1].copy()
        patch[..., 3] = np.where(patch[..., 3] < ALPHA_KEEP, 0, patch[..., 3])
        bb = body_box(patch)
        if bb is None:
            raise SystemExit(f"{tag}/{stem}#{item['index']} has no solid body")
        frames.append({"index": item["index"], "row": row, "col": col, "patch": patch,
                       "x0": x0, "y0": y0, "body": bb,
                       "explicit_anchor_x": float(item["anchor_x"]) if explicit else None})

    # One ground line per row; one anchor column per cell.
    ground: Dict[int, float] = {}
    for f in frames:
        gy = f["y0"] + f["body"][3]
        ground[f["row"]] = max(ground.get(f["row"], 0), gy)
    for f in frames:
        f["anchor_x"] = f["explicit_anchor_x"] if f["explicit_anchor_x"] is not None             else (f["col"] + 0.5) * cell_w
        f["anchor_y"] = ground[f["row"]]

    # Canvas: the widest reach any frame needs from its anchor, in all four
    # directions, so every frame shares one geometry.
    # CEIL, not round. The anchor is a float once a slicer supplies a fitted one, so a
    # frame can need a fraction of a pixel more room than the rounded extent reserves --
    # which lands as `could not broadcast (453,377) into (453,376)` on whichever frame sits
    # furthest from its rung. Ceiling every extent costs at most one transparent column and
    # cannot under-reserve.
    left = max(int(math.ceil(f["anchor_x"] - f["x0"])) for f in frames)
    right = max(int(math.ceil(f["x0"] + f["patch"].shape[1] - f["anchor_x"])) for f in frames)
    up = max(int(math.ceil(f["anchor_y"] - f["y0"])) for f in frames)
    down = max(int(math.ceil(f["y0"] + f["patch"].shape[0] - f["anchor_y"])) for f in frames)

    canvas_w, canvas_h = left + right, up + down
    out = []
    for f in sorted(frames, key=lambda x: x["index"]):
        canvas = np.zeros((canvas_h, canvas_w, 4), dtype=np.uint8)
        px = int(math.floor(f["x0"] - f["anchor_x"] + left))
        py = int(math.floor(f["y0"] - f["anchor_y"] + up))
        ph, pw = f["patch"].shape[:2]
        canvas[py:py + ph, px:px + pw] = f["patch"]
        out.append(canvas)

    tallest_body = max(f["body"][3] - f["body"][1] for f in frames)
    return out, tallest_body


def build_idle_canvases(slices_root: str, tag: str, stem: str,
                        poses: Dict[int, str], mirror_from: Dict[str, str]):
    """A turnaround idle: one authored (or mirrored) pose per direction.

    Returns ``(canvases_by_direction, tallest_body_px)`` covering all 8 directions.
    """
    manifest = load_slices(slices_root, stem)
    sheet = np.asarray(Image.open(manifest["source"]).convert("RGBA"))
    by_index = {it["index"]: it for it in manifest["items"]}

    raw_poses: Dict[str, dict] = {}
    for index, direction in poses.items():
        if index not in by_index:
            raise SystemExit(f"{tag}/{stem}: turnaround pose #{index} was not sliced")
        x0, y0, x1, y1 = by_index[index]["sheet_box"]
        patch = sheet[y0:y1, x0:x1].copy()
        patch[..., 3] = np.where(patch[..., 3] < ALPHA_KEEP, 0, patch[..., 3])
        bb = body_box(patch)
        if bb is None:
            raise SystemExit(f"{tag}/{stem}#{index} has no solid body")
        raw_poses[direction] = {"patch": patch, "body": bb, "mirror": False}

    for direction, source in mirror_from.items():
        if source not in raw_poses:
            raise SystemExit(f"{tag}: mirrorFrom['{direction}'] = '{source}', "
                             f"but '{source}' has no authored pose")
        raw_poses[direction] = {**raw_poses[source], "mirror": True}

    missing = VALID_DIRECTIONS - set(raw_poses)
    if missing:
        raise SystemExit(f"{tag}: idle has no pose for {sorted(missing)} - "
                         "add it to 'poses' or 'mirrorFrom'")

    # A static pose carries no motion to preserve, so anchor it on the body
    # itself: feet on the canvas floor, hips on the canvas centre line.
    def cx(p): return int(round((p["body"][0] + p["body"][2]) / 2))

    left = max(cx(p) for p in raw_poses.values())
    right = max(p["patch"].shape[1] - cx(p) for p in raw_poses.values())
    up = max(p["body"][3] for p in raw_poses.values())
    down = max(p["patch"].shape[0] - p["body"][3] for p in raw_poses.values())

    canvas_w, canvas_h = left + right, up + down
    out: Dict[str, np.ndarray] = {}
    for direction, p in raw_poses.items():
        canvas = np.zeros((canvas_h, canvas_w, 4), dtype=np.uint8)
        px = left - cx(p)
        py = up - p["body"][3]
        ph, pw = p["patch"].shape[:2]
        canvas[py:py + ph, px:px + pw] = p["patch"]
        if p["mirror"]:
            canvas = canvas[:, ::-1, :]
        out[direction] = canvas

    tallest = max(p["body"][3] - p["body"][1] for p in raw_poses.values())
    return out, tallest


# --------------------------------------------------------------------------
# Manifest emission
# --------------------------------------------------------------------------

def _repo_relative(path: str) -> str:
    try:
        return os.path.relpath(path, REPO).replace("\\", "/")
    except ValueError:
        return os.path.abspath(path).replace("\\", "/")


def _unity_asset_path(png_path: str) -> str:
    """Repo-relative path with the ``unity/Valkur/`` prefix stripped to ``Assets/...``.

    That is the form ``AssetDatabase.LoadAssetAtPath`` expects. A path that is not
    under the Unity project's Assets folder is returned repo-relative unchanged -
    callers that care (the default run) reject that with ``--allow-outside-assets``
    required to proceed.
    """
    rel = _repo_relative(png_path)
    if rel.startswith(ASSETS_PREFIX):
        return "Assets/" + rel[len(ASSETS_PREFIX):]
    return rel


def validate_monster_key(key: str) -> List[str]:
    errors = []
    if not key:
        errors.append("empty monsterKey")
    elif set(key) - NAME_ALLOWED:
        bad = "".join(sorted(set(key) - NAME_ALLOWED))
        errors.append(f"monsterKey '{key}' has illegal characters '{bad}'")
    return errors


def validate_direction_map(tag: str, direction_map: Dict[str, str]) -> List[str]:
    errors = []
    for d, side in direction_map.items():
        if d not in VALID_DIRECTIONS:
            errors.append(f"{tag}: directionMap has unknown direction '{d}'")
        if side not in ("west", "east"):
            errors.append(f"{tag}: directionMap['{d}'] = '{side}' is neither 'west' nor 'east'")
    missing = VALID_DIRECTIONS - set(direction_map)
    if missing:
        errors.append(f"{tag}: directionMap is missing {sorted(missing)}")
    return errors


def process_monster(key: str, cfg: dict, out_root: str, allow_outside_assets: bool,
                    dry_run: bool) -> Tuple[dict, List[str]]:
    """Builds every configured state + idle for one monster.

    Returns ``(manifest_entry, written_summary_lines)``.
    """
    errors = validate_monster_key(key)
    direction_map = cfg.get("directionMap", DEFAULT_DIRECTION_MAP)
    errors += validate_direction_map(key, direction_map)
    if errors:
        raise SystemExit("\n".join(f"ERROR {e}" for e in errors))

    slices_root = os.path.join(REPO, cfg["slicesRoot"]) if not os.path.isabs(cfg["slicesRoot"]) \
        else cfg["slicesRoot"]
    out_dir = cfg.get("outDir")
    out_dir = os.path.join(REPO, out_dir) if out_dir and not os.path.isabs(out_dir) else out_dir
    out_dir = out_dir or os.path.join(out_root, key)
    target_body_px = float(cfg.get("targetBodyPx", DEFAULT_TARGET_BODY_PX))

    if not dry_run:
        os.makedirs(out_dir, exist_ok=True)

    lines: List[str] = []
    entry = {"monsterKey": key, "displayName": cfg.get("displayName", key.replace("_", " ").title()),
             "idle": [], "states": [], "attackVariants": [], "castVariants": [],
             "targetBodyPx": target_body_px}

    idle_cfg = cfg.get("idle")
    if idle_cfg:
        poses = {int(k): v for k, v in idle_cfg["poses"].items()}
        bad_dirs = [v for v in poses.values() if v not in VALID_DIRECTIONS]
        if bad_dirs:
            raise SystemExit(f"{key}: idle poses name unknown direction(s) {bad_dirs}")
        canvases, body_px = build_idle_canvases(
            slices_root, key, idle_cfg["sheet"], poses, idle_cfg.get("mirrorFrom", {}))
        scale = target_body_px / body_px
        for direction in DIRECTIONS:
            img = resample(canvases[direction], scale)
            name = f"{key}_idle_{direction.lower()}"
            png_path = os.path.join(out_dir, f"{name}.png")
            if not dry_run:
                img.save(png_path)
            entry["idle"].append({"direction": direction, "path": _unity_asset_path(png_path)})
        lines.append(f"idle: 8 directions, body {body_px:.0f}px -> {target_body_px:.0f}px (x{scale:.3f})")

    def emit_cycle(slot: str, state_cfg: dict):
        """One cyclic animation: cut, scale, mirror, and fill the eight direction buckets.

        Shared by the seven base states and by every attack/cast VARIANT, because a
        variant is the same animation in every respect that matters here - it differs
        only in which list on EntityAssetConfig it lands in. A second copy of this loop
        for variants is the shape that drifts.
        """
        stem = state_cfg["sheet"]
        cols = int(state_cfg.get("cols", 1))
        rows = int(state_cfg.get("rows", 1))
        mirror = bool(state_cfg.get("mirror", True))
        canvases, body_px = build_state_canvases(slices_root, key, stem, cols, rows)

        # `scaleAdjust` is the SCALE_OVERRIDE escape hatch the player pipeline documents,
        # and it exists for one failure: `body_px` is the tallest body across the state, so
        # a sheet whose tallest frame throws its arms overhead measures tall for its size
        # and the whole animation is rendered too small. The tell is that the sheet's HEIGHT
        # ratio and its AREA ratio against the same monster's idle disagree — measured on
        # this wave, they agree within 6 % on twelve of thirteen barbol_muscle sheets and by
        # 26 % on `spellcast_5`, which is the one that raises both arms.
        scale = target_body_px / body_px * float(state_cfg.get("scaleAdjust", 1.0))

        west_paths: List[str] = []
        east_paths: List[str] = []
        for i, canvas in enumerate(canvases):
            img = resample(canvas, scale)
            png_w = os.path.join(out_dir, f"{key}_{slot}_w{i}.png")
            if not dry_run:
                img.save(png_w)
            west_paths.append(_unity_asset_path(png_w))

            if mirror:
                png_e = os.path.join(out_dir, f"{key}_{slot}_e{i}.png")
                if not dry_run:
                    img.transpose(Image.FLIP_LEFT_RIGHT).save(png_e)
                east_paths.append(_unity_asset_path(png_e))

        sprites: List[str] = []
        for direction in DIRECTIONS:
            side = direction_map[direction] if mirror else "west"
            sprites.extend(east_paths if side == "east" else west_paths)

        mirror_note = "west + mirrored east" if mirror else "west only (no mirror)"
        lines.append(f"{slot:16s}: {len(canvases)} frames ({mirror_note}), "
                     f"body {body_px:.0f}px -> {target_body_px:.0f}px (x{scale:.3f}), "
                     f"frame {resample(canvases[0], scale).size}")
        return len(canvases), sprites

    for state, state_cfg in cfg.get("states", {}).items():
        frames_per_direction, sprites = emit_cycle(state, state_cfg)
        entry["states"].append({
            "state": state,
            "framesPerDirection": frames_per_direction,
            "sprites": sprites,
        })

    # Extra attacks are VARIANTS, never new AnimState values - CLAUDE.md records why: the
    # seven states are enumerated positionally in four independent places, so an eighth
    # pays that tax four times over, and PlayerController's locomotion whitelist would
    # enter a state it never leaves. A variant index under the existing Attack state
    # inherits both whitelists by construction.
    for group, out_key in (("attackVariants", "attackVariants"),
                           ("castVariants", "castVariants")):
        for variant in cfg.get(group, []):
            slot = f"{out_key[:-8]}_{variant['key']}"   # e.g. attack_kick / cast_roar
            frames_per_direction, sprites = emit_cycle(slot, variant)
            entry.setdefault(out_key, []).append({
                "key": variant["key"],
                "framesPerDirection": frames_per_direction,
                "sprites": sprites,
            })

    if not allow_outside_assets:
        bad = [p["path"] for p in entry["idle"] if not p["path"].startswith("Assets/")]
        for group in (entry["states"], entry["attackVariants"], entry["castVariants"]):
            for s in group:
                bad += [p for p in s["sprites"] if not p.startswith("Assets/")]
        if bad:
            raise SystemExit(
                f"{key}: outDir does not resolve under Assets/ ({sorted(set(bad))[:1]}) - "
                "pass --allow-outside-assets only for a scratch/smoke run, never for a real import.")

    return entry, lines


# --------------------------------------------------------------------------
# Entry point
# --------------------------------------------------------------------------

def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--config", required=True, help="JSON describing one or more monsters")
    ap.add_argument("--manifest", default=DEFAULT_MANIFEST,
                    help="Where to write the manifest MonsterFramesImporter reads. "
                         "Give every wave its own name (e.g. *_wave2.json) - an existing "
                         "file is never overwritten unless --overwrite is passed.")
    ap.add_argument("--out-root", default=DEFAULT_OUT_ROOT,
                    help="Base directory for a monster with no explicit 'outDir' "
                         "(default: Art/NPC/monsters/)")
    ap.add_argument("--allow-outside-assets", action="store_true",
                    help="Permit output paths outside unity/Valkur/Assets/ - "
                         "only for a scratch smoke-test run; MonsterFramesImporter "
                         "cannot resolve such a manifest.")
    ap.add_argument("--dry-run", action="store_true", help="Compute sizes; write nothing")
    ap.add_argument("--overwrite", action="store_true",
                    help="Allow overwriting an existing manifest file")
    args = ap.parse_args()

    with open(args.config, encoding="utf-8") as fh:
        config = json.load(fh)

    if not args.dry_run and os.path.exists(args.manifest) and not args.overwrite:
        print(f"ERROR {args.manifest} already exists. Give this wave its own filename "
              "(e.g. monster_frames_manifest_wave2.json) or pass --overwrite.", file=sys.stderr)
        return 1

    monsters = config.get("monsters", {})
    if not monsters:
        print("ERROR config has no 'monsters' entries", file=sys.stderr)
        return 1

    entries = []
    for key, cfg in monsters.items():
        entry, lines = process_monster(key, cfg, args.out_root, args.allow_outside_assets, args.dry_run)
        entries.append(entry)
        print(f"{key}:")
        for line in lines:
            print(f"  {line}")

    manifest = {
        "generator": "tools/atlas/build_monster_frames.py",
        "generatedFrom": _repo_relative(args.config),
        "monsters": entries,
    }

    if not args.dry_run:
        os.makedirs(os.path.dirname(args.manifest), exist_ok=True)
        with open(args.manifest, "w", encoding="utf-8") as fh:
            json.dump(manifest, fh, indent=2)
        print(f"\n{len(entries)} monster(s) -> {args.manifest}")
    else:
        print(f"\n(dry run) {len(entries)} monster(s) would be written")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
