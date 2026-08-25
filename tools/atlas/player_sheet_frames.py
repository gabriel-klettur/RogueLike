#!/usr/bin/env python3
"""Explode a player character sheet into single frames, and stitch them back.

The character sheets under ``Art/Characters/<class>/`` are single-row strips of
128x128 frames (``valkyrie_idle.png`` is 5248x128 = 41 frames).  Retouching one
pose inside a 5000 px strip is painful, so this tool round-trips the strip:

    extract   strip PNG      -> frames/<class>_<state>_<NNN>.png  (+ sheet.json)
    restitch  frames + json  -> strip PNG, byte-for-byte same geometry

Why the geometry must not move
------------------------------
``PlayerCharacterAssetBinder`` re-slices the strip on a fixed 128 px grid and
derives every sprite's GUID from ``<texturePath>#<spriteName>``, so the 284
sprite references inside ``Data/Catalogs/Players/<class>.asset`` survive a
retouch **as long as the file name, the frame size and the frame count stay the
same**.  Change the width and the last frames silently vanish from the catalog.
``restitch`` therefore refuses to write a strip whose dimensions differ from the
ones recorded at extract time.

Usage
-----
    python player_sheet_frames.py extract  --class valkyrie [--state idle] --out <dir>
    python player_sheet_frames.py restitch --dir <dir>/<class>_<state> [--dry-run]
"""

from __future__ import annotations

import argparse
import json
import os
import sys

from PIL import Image

FRAME_PX = 128
STATES = ("idle", "walking", "casting")
CHARACTERS_ROOT = os.path.join(
    "unity", "Valkur", "Assets", "_Project", "Art", "Characters"
)
SIDECAR = "sheet.json"


def _repo_root() -> str:
    """Repo root, so the tool works from anywhere."""
    return os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))


def _sheet_path(class_key: str, state: str) -> str:
    return os.path.join(
        _repo_root(), CHARACTERS_ROOT, class_key, f"{class_key}_{state}.png"
    )


def extract(class_key: str, states: list[str], out_root: str) -> int:
    for state in states:
        src = _sheet_path(class_key, state)
        if not os.path.exists(src):
            print(f"  SKIP {class_key}_{state}: no sheet at {src}")
            continue

        img = Image.open(src).convert("RGBA")
        if img.height != FRAME_PX or img.width % FRAME_PX:
            print(
                f"  FAIL {class_key}_{state}: {img.width}x{img.height} is not a "
                f"whole number of {FRAME_PX}px frames"
            )
            return 1

        count = img.width // FRAME_PX
        out_dir = os.path.join(out_root, f"{class_key}_{state}")
        os.makedirs(out_dir, exist_ok=True)

        for i in range(count):
            box = (i * FRAME_PX, 0, (i + 1) * FRAME_PX, FRAME_PX)
            # Plain crop: no resample, no mode change, so a frame the artist
            # leaves untouched restitches to the exact original pixels.
            img.crop(box).save(
                os.path.join(out_dir, f"{class_key}_{state}_{i:03d}.png")
            )

        with open(os.path.join(out_dir, SIDECAR), "w", encoding="utf-8") as fh:
            json.dump(
                {
                    "class": class_key,
                    "state": state,
                    "target": os.path.relpath(src, _repo_root()).replace("\\", "/"),
                    "frame": FRAME_PX,
                    "count": count,
                    "width": img.width,
                    "height": img.height,
                },
                fh,
                indent=2,
            )

        print(f"  {class_key}_{state}: {count} frames -> {out_dir}")
    return 0


def restitch(frames_dir: str, dry_run: bool) -> int:
    sidecar = os.path.join(frames_dir, SIDECAR)
    if not os.path.exists(sidecar):
        print(f"FAIL: no {SIDECAR} in {frames_dir} — was it produced by extract?")
        return 1

    with open(sidecar, encoding="utf-8") as fh:
        meta = json.load(fh)

    strip = Image.new("RGBA", (meta["width"], meta["height"]), (0, 0, 0, 0))
    for i in range(meta["count"]):
        name = f"{meta['class']}_{meta['state']}_{i:03d}.png"
        path = os.path.join(frames_dir, name)
        if not os.path.exists(path):
            print(f"FAIL: frame {name} is missing — every frame must be present")
            return 1

        frame = Image.open(path).convert("RGBA")
        if frame.size != (FRAME_PX, FRAME_PX):
            print(
                f"FAIL: {name} is {frame.width}x{frame.height}, must stay "
                f"{FRAME_PX}x{FRAME_PX} or the Unity slice grid breaks"
            )
            return 1

        strip.paste(frame, (i * FRAME_PX, 0))

    dst = os.path.join(_repo_root(), meta["target"])
    if dry_run:
        print(f"DRY-RUN would write {strip.width}x{strip.height} -> {dst}")
        return 0

    strip.save(dst)
    print(f"wrote {strip.width}x{strip.height} ({meta['count']} frames) -> {dst}")
    print("Next: Unity 'Valkur/Setup/Rebuild Player Character Assets' to re-slice.")
    return 0


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    sub = ap.add_subparsers(dest="cmd", required=True)

    ex = sub.add_parser("extract", help="strip -> frames")
    ex.add_argument("--class", dest="class_key", required=True)
    ex.add_argument("--state", choices=STATES, help="default: every state")
    ex.add_argument("--out", required=True, help="output root directory")

    re_ = sub.add_parser("restitch", help="frames -> strip")
    re_.add_argument("--dir", required=True, help="one <class>_<state> frames dir")
    re_.add_argument("--dry-run", action="store_true")

    args = ap.parse_args()
    if args.cmd == "extract":
        states = [args.state] if args.state else list(STATES)
        return extract(args.class_key, states, args.out)
    return restitch(args.dir, args.dry_run)


if __name__ == "__main__":
    sys.exit(main())
