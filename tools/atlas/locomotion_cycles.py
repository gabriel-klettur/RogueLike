"""Measure every player locomotion cycle: where its loop starts, which frames plant a foot,
and how long a step is — the data that lets the runtime keep the feet planted.

Writes tools/atlas/generated/locomotion_cycles.json, keyed by SHEET (the frames' name prefix,
e.g. "dwarf_armed_running"), because the same sheet is reached from a base state, a state
variant, a loadout and a Dark twin, and the cycle is a fact about the drawing, not about which
of those reached it. Also writes one contact sheet per cycle under
tools/atlas/generated/locomotion_cycles/ with the loop start and the contacts marked, so every
number is REVIEWED BY EYE before `Valkur > Players > Import Locomotion Cycles` applies it — the
same split the cast-muzzle baker and the tree-trunk manifest use.

Per cycle (the east `_e` half; the west half is its mirror and shares every number):

    loopStart     0 or 1. The old animator skipped frame 0 of every walk and run (a rule from
                  the Python build, where frame 0 was a standing pose). Measured instead: the
                  loop starts at 1 only when the last frame joins frame 1 clearly better than
                  frame 0 (alpha IoU margin).
    contactFrames Two frames, one per foot: the widest leg span among frames with the feet on
                  the ground line, half a cycle apart.
    strideUnits   One step in world units: the swing of the leg span, x0.9, clamped to a sane
                  fraction of the body height (flagged when the clamp bites — the AI-drawn
                  cycles do not always plant their feet).
    groundLiftPx  How far each frame's lowest pixel floats above the ground line (reported).

Usage:
    python tools/atlas/locomotion_cycles.py            # measure, write JSON + contact sheets
    python tools/atlas/locomotion_cycles.py --check    # exit 1 if the JSON is stale
"""
from __future__ import annotations

import argparse
import json
import os
import sys

import numpy as np
from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
UNITY = os.path.join(ROOT, "unity", "Valkur")
MANIFEST = os.path.join(ROOT, "tools", "atlas", "generated", "player_frames_manifest_wave3.json")
OUT_JSON = os.path.join(ROOT, "tools", "atlas", "generated", "locomotion_cycles.json")
OUT_SHEETS = os.path.join(ROOT, "tools", "atlas", "generated", "locomotion_cycles")

ALPHA = 128
LEG_BAND = 0.22            # lower fraction of the body the leg span is measured in
ON_GROUND_PX = 2           # a frame whose lowest pixel is this close to the canvas bottom is grounded
LOOP_MARGIN = 0.05         # frame 1 must join the last frame this much better than frame 0
STRIDE_SPAN_FACTOR = 0.9
STRIDE_MIN_BODY = {"walk": 0.18, "chase": 0.30}
STRIDE_MAX_BODY = {"walk": 0.45, "chase": 0.75}
DEFAULT_PPU = 64


def mask(path):
    return np.asarray(Image.open(path).convert("RGBA"))[:, :, 3] >= ALPHA


def iou(a, b):
    return float((a & b).sum()) / max(1, int((a | b).sum()))


def sheet_key(sprite_path):
    name = os.path.splitext(os.path.basename(sprite_path))[0]
    return name.rsplit("_", 1)[0]  # strip "_e3"


def east_frames(sprites):
    frames = sorted({s for s in sprites if "_e" in os.path.basename(s)},
                    key=lambda s: int(os.path.splitext(s)[0].rsplit("_e", 1)[1]))
    return frames


def collect_cycles(player):
    """Every (sheetKey, state, frames) a walk or chase can show, base, variant or loadout."""
    found = {}

    def add(state, sprites):
        if state not in ("walk", "chase") or not sprites:
            return
        frames = east_frames(sprites)
        if frames:
            found.setdefault(sheet_key(frames[0]), (state, frames))

    containers = [player] + list(player.get("loadouts") or [])
    for c in containers:
        for s in c.get("states") or []:
            add(s["state"], s.get("sprites"))
        for g in c.get("stateVariants") or []:
            for v in g.get("variants") or []:
                add(g.get("state"), v.get("sprites"))
    return found


def measure(state, frames, ppu):
    masks = [mask(os.path.join(UNITY, f.replace("/", os.sep))) for f in frames]
    n = len(masks)
    h = masks[0].shape[0]
    lifts, spans, heights = [], [], []
    for m in masks:
        rows = np.where(m.any(axis=1))[0]
        top, bottom = int(rows[0]), int(rows[-1])
        heights.append(bottom - top)
        lifts.append(h - 1 - bottom)
        strip = m[int(bottom - (bottom - top) * LEG_BAND):bottom + 1]
        cols = np.where(strip.any(axis=0))[0]
        spans.append(int(cols[-1] - cols[0]))

    loop_start = 1 if n > 2 and iou(masks[-1], masks[1]) > iou(masks[-1], masks[0]) + LOOP_MARGIN else 0
    loop = list(range(loop_start, n))
    cycle = len(loop)

    # Grounded relative to the cycle's own lowest frame: several cycles float a few pixels over
    # their whole length, and an absolute test excluded exactly their best contact frames.
    floor = min(lifts[i] for i in loop)
    grounded = set(i for i in loop if lifts[i] <= floor + ON_GROUND_PX)
    # A two-step cycle is symmetric: the second foot lands half a cycle after the first. Choose
    # the pair (a, a + cycle/2) with the widest stride, preferring grounded frames.
    half = cycle // 2
    best = None
    for k in range(cycle):
        a, b = loop[k], loop[(k + half) % cycle]
        score = spans[a] + spans[b] + (1000 if a in grounded and b in grounded else 0)
        if best is None or score > best[0]:
            best = (score, a, b)
    contacts = sorted([best[1], best[2]])

    body = float(np.median(heights))
    step_px = (max(spans[i] for i in loop) - min(spans[i] for i in loop)) * STRIDE_SPAN_FACTOR
    lo, hi = STRIDE_MIN_BODY[state] * body, STRIDE_MAX_BODY[state] * body
    clamped = step_px < lo or step_px > hi
    step_px = min(max(step_px, lo), hi)

    return {
        "state": state,
        "frames": n,
        "loopStart": loop_start,
        "contactFrames": contacts,
        "strideUnits": round(step_px / ppu, 3),
        "strideClamped": clamped,
        "bodyUnits": round(body / ppu, 3),
        "groundLiftPx": lifts,
        "legSpanPx": spans,
    }


def draw_sheet(key, frames, data):
    ims = [Image.open(os.path.join(UNITY, f.replace("/", os.sep))).convert("RGBA") for f in frames]
    w, h = ims[0].size
    sheet = Image.new("RGBA", (w * len(ims), h + 16), (38, 38, 42, 255))
    d = ImageDraw.Draw(sheet)
    for i, im in enumerate(ims):
        x = i * w
        if i in data["contactFrames"]:
            d.rectangle([x, 0, x + w - 1, h - 1], fill=(30, 80, 40, 255))
        sheet.alpha_composite(im, (x, 0))
        d.line([(x, 0), (x, h)], fill=(120, 120, 120, 255))
        d.line([(x, h - 1), (x + w, h - 1)], fill=(0, 220, 0, 255))
        label = f"{i}{' C' if i in data['contactFrames'] else ''}{' skip' if i < data['loopStart'] else ''}"
        d.text((x + 2, h + 2), label, fill=(255, 230, 0, 255))
    os.makedirs(OUT_SHEETS, exist_ok=True)
    sheet.save(os.path.join(OUT_SHEETS, key + ".png"))


def build():
    with open(MANIFEST, encoding="utf-8") as f:
        manifest = json.load(f)
    ppu_map = manifest.get("characterPpu") or {}
    result = {}
    for player in manifest["players"]:
        ppu = ppu_map.get(player["playerKey"], DEFAULT_PPU)
        for key, (state, frames) in sorted(collect_cycles(player).items()):
            data = measure(state, frames, ppu)
            data["player"] = player["playerKey"]
            result[key] = data
            draw_sheet(key, frames, data)
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    result = build()
    # "list" is the same data as an array: the shape Unity's JsonUtility can read.
    listing = [{"key": k, "state": d["state"], "loopStart": d["loopStart"],
                "contactFrames": d["contactFrames"], "strideUnits": d["strideUnits"],
                "groundLiftPx": d["groundLiftPx"]}
               for k, d in result.items()]
    text = json.dumps({"generator": "tools/atlas/locomotion_cycles.py", "cycles": result,
                       "list": listing}, indent=2) + "\n"
    if args.check:
        current = open(OUT_JSON, encoding="utf-8").read() if os.path.exists(OUT_JSON) else ""
        if current != text:
            print("locomotion_cycles.json is stale")
            return 1
        return 0
    with open(OUT_JSON, "w", encoding="utf-8") as f:
        f.write(text)
    for key, d in result.items():
        print(f"{key:28s} {d['state']:5s} loop@{d['loopStart']} contacts={d['contactFrames']} "
              f"stride={d['strideUnits']}u{' (clamped)' if d['strideClamped'] else ''} body={d['bodyUnits']}u lift={d['groundLiftPx']}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
