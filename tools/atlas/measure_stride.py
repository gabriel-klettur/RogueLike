"""Measure the speed each player locomotion cycle was DRAWN for.

A walk or run cycle only looks planted when the body moves at the speed the art implies: the
foot on the ground slides backwards across the canvas by some number of pixels per frame, and
the body has to cover exactly that distance in the same time or the feet skate (too fast) or
stamp in place (too slow). This script measures that backwards slide on the shipped frames and
turns it into world units per second:

    speed = median backward foot slide (px / frame) / PPU / frame interval

Valkur's DirectionalAnimator plays walk and chase (run) at 0.15 s per frame, looping frames
1..end, and the frames are baked with the ground line at the canvas bottom and the art facing
EAST in the `_e` half. PPU comes from the manifest's `characterPpu` (64 by default).

The numbers feed `EntityAssetConfig.walkReferenceSpeed` / `runReferenceSpeed`; the runtime
divides the body's real speed by them to pace the cycle (DirectionalAnimator.Locomotion.cs).

MEASURED 2026-09-14 AND NOT APPLIED. On the shipped roster the numbers are not trustworthy:
dwarf walk 0.34 u/s against a class speed of 4, vampire run 1.06 u/s (slower than her walk),
mague run 0.78 u/s. The wave3 frames are AI-drawn cycles aligned on the CELL centre, so the
feet sway around a fixed point instead of sliding a consistent stride, and the ground-band
heuristic picks up robes, capes and weapon tips. Every class already moves 2-10x faster than its
art implies, so the runtime keeps the DERIVED references (walk = class speed, run = class speed
x runReferenceFactor), which preserves the authored frame rate at a walk. Re-run and review by
eye on a contact sheet before writing any value into an EntityAssetConfig.

Usage:
    python tools/atlas/measure_stride.py            # report every player
    python tools/atlas/measure_stride.py --json     # machine-readable, for the importer probe
"""
from __future__ import annotations

import argparse
import json
import os
import statistics
import sys

import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
UNITY = os.path.join(ROOT, "unity", "Valkur")
MANIFEST = os.path.join(ROOT, "tools", "atlas", "generated", "player_frames_manifest_wave3.json")

FRAME_INTERVAL = 0.15
DEFAULT_PPU = 64
ALPHA = 128
# Bottom band treated as "touching the ground", as a fraction of the tallest frame's body height.
GROUND_BAND = 0.045
# A foot cluster is matched to the nearest one in the next frame within this fraction of width.
MATCH_FRACTION = 0.35


def load_alpha(path: str) -> np.ndarray:
    img = Image.open(path).convert("RGBA")
    return np.asarray(img)[:, :, 3] >= ALPHA


def body_height(mask: np.ndarray) -> int:
    rows = np.where(mask.any(axis=1))[0]
    return int(rows[-1] - rows[0] + 1) if rows.size else 0


def contact_clusters(mask: np.ndarray, band: int) -> list[float]:
    """Centres of the column runs of opaque pixels in the bottom band."""
    h = mask.shape[0]
    rows = np.where(mask.any(axis=1))[0]
    if rows.size == 0:
        return []
    bottom = int(rows[-1])
    strip = mask[max(0, bottom - band + 1): bottom + 1, :]
    cols = np.where(strip.any(axis=0))[0]
    if cols.size == 0:
        return []
    clusters, start, prev = [], cols[0], cols[0]
    for c in cols[1:]:
        if c - prev > 2:
            clusters.append((start + prev) / 2.0)
            start = c
        prev = c
    clusters.append((start + prev) / 2.0)
    return clusters


def measure_cycle(paths: list[str]) -> float | None:
    """Median backward slide of the ground contact, px per frame, over the looped cycle."""
    masks = [load_alpha(p) for p in paths]
    if len(masks) < 3:
        return None
    height = max(body_height(m) for m in masks)
    width = masks[0].shape[1]
    band = max(2, int(round(height * GROUND_BAND)))
    # Walk/run loop frames 1..end, wrapping from the last straight back to frame 1.
    order = list(range(1, len(masks))) + [1]
    slides = []
    for a, b in zip(order, order[1:]):
        ca, cb = contact_clusters(masks[a], band), contact_clusters(masks[b], band)
        for x in ca:
            if not cb:
                continue
            nearest = min(cb, key=lambda y: abs(y - x))
            dx = nearest - x
            if abs(dx) <= width * MATCH_FRACTION and dx < -0.25:
                slides.append(-dx)
    if not slides:
        return None
    return statistics.median(slides)


def east_frames(sprites: list[str]) -> list[str]:
    seen, out = set(), []
    for s in sprites:
        name = os.path.basename(s)
        if "_e" not in name or s in seen:
            continue
        seen.add(s)
        out.append(s)
    out.sort(key=lambda p: int(os.path.splitext(p)[0].rsplit("_e", 1)[1]))
    return out


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--json", action="store_true")
    args = parser.parse_args()

    with open(MANIFEST, encoding="utf-8") as f:
        manifest = json.load(f)
    ppu_map = manifest.get("characterPpu", {}) or {}

    report = {}
    for player in manifest["players"]:
        key = player["playerKey"]
        ppu = ppu_map.get(key, DEFAULT_PPU)
        entry = {"ppu": ppu}
        for state_name, field in (("walk", "walkReferenceSpeed"), ("chase", "runReferenceSpeed")):
            state = next((s for s in player["states"] if s["state"] == state_name), None)
            if state is None:
                continue
            frames = [os.path.join(UNITY, p.replace("/", os.sep)) for p in east_frames(state["sprites"])]
            px = measure_cycle(frames)
            if px is None:
                continue
            entry[field] = round(px / ppu / FRAME_INTERVAL, 2)
            entry[field + "_pxPerFrame"] = round(px, 2)
        report[key] = entry

    if args.json:
        print(json.dumps(report, indent=2))
    else:
        for key, e in report.items():
            print(f"{key:10s} ppu={e['ppu']:3d}  walk={e.get('walkReferenceSpeed', '-')} u/s "
                  f"({e.get('walkReferenceSpeed_pxPerFrame', '-')} px/f)  "
                  f"run={e.get('runReferenceSpeed', '-')} u/s ({e.get('runReferenceSpeed_pxPerFrame', '-')} px/f)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
