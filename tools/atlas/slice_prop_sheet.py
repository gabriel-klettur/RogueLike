#!/usr/bin/env python3
"""Slice a multi-object prop sheet into individual sprite PNGs.

The AI-generated prop sheets under ``unity/downloads/assets`` already carry a real
alpha channel: the objects are opaque (alpha ~250) and the "background" is either
fully transparent or a soft glow halo (alpha 20..200).  So slicing is a
segmentation problem on the alpha channel, not a chroma-key problem.

Algorithm
---------
1. ``core``  = alpha >= ``core_threshold``  -> the solid body of every object.
2. Label the cores, drop specks below ``min_core_area``.
3. ``full``  = alpha >= ``edge_threshold``  -> body + soft edges + glow.
4. Every ``full`` pixel is assigned to the *nearest* core via a Euclidean
   distance transform, capped at ``max_glow_dist``.  This is what keeps a
   brazier's soft flame attached to its own bowl while refusing to let two
   neighbouring glows bridge into one blob.
5. Boxes that overlap heavily are merged (an object whose body is split by a
   thin low-alpha neck comes back as one).
6. Boxes are sorted in reading order (rows top-to-bottom, then left-to-right).

Everything after step 4 can be corrected by hand from the sheet config
(``drop`` / ``merge`` / ``split`` / ``boxes``) because no automatic pass gets
198 hand-drawn objects right on its own.

Usage
-----
    python slice_prop_sheet.py --sheet <png> --out <dir> [--config <json>]
    python slice_prop_sheet.py --all --out <dir>          # every configured sheet
"""

from __future__ import annotations

import argparse
import json
import os
import sys
from dataclasses import dataclass, field, asdict

import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

# --------------------------------------------------------------------------
# Defaults
# --------------------------------------------------------------------------

DEFAULTS = {
    # Alpha at or above this is the solid body of an object.
    "core_threshold": 190,
    # Alpha at or above this is kept in the exported crop (soft edges + glow).
    "edge_threshold": 16,
    # A core component smaller than this many pixels is noise.
    "min_core_area": 700,
    # A soft pixel further than this from any core is stray glow, not part of
    # an object.  Measured in pixels.
    "max_glow_dist": 26.0,
    # Alpha below this is flattened to 0 in the exported crop, so the faint
    # gradient haze never shows up as a grey rectangle in game.
    "haze_cutoff": 12,
    # Two boxes are merged when their intersection covers this fraction of the
    # smaller box.
    "merge_overlap": 0.55,
    # Extra transparent margin kept around each crop before the tight trim.
    "pad": 4,
    # Row clustering tolerance as a fraction of the median box height.
    "row_tolerance": 0.55,
    # An island under this many solid pixels can be a speck ...
    "speck_area": 500,
    # ... and only if it is also this small a fraction of the object's own mass.
    "speck_fraction": 0.04,
}


@dataclass
class Box:
    """Axis-aligned box in sheet pixel space, ``x1``/``y1`` exclusive."""

    x0: int
    y0: int
    x1: int
    y1: int

    @property
    def w(self) -> int:
        return self.x1 - self.x0

    @property
    def h(self) -> int:
        return self.y1 - self.y0

    @property
    def area(self) -> int:
        return max(0, self.w) * max(0, self.h)

    @property
    def cx(self) -> float:
        return (self.x0 + self.x1) * 0.5

    @property
    def cy(self) -> float:
        return (self.y0 + self.y1) * 0.5

    def union(self, other: "Box") -> "Box":
        return Box(min(self.x0, other.x0), min(self.y0, other.y0),
                   max(self.x1, other.x1), max(self.y1, other.y1))

    def intersection_area(self, other: "Box") -> int:
        w = min(self.x1, other.x1) - max(self.x0, other.x0)
        h = min(self.y1, other.y1) - max(self.y0, other.y0)
        return max(0, w) * max(0, h)

    def as_list(self) -> list[int]:
        return [self.x0, self.y0, self.x1, self.y1]


@dataclass
class SheetConfig:
    """Per-sheet tuning plus the manual corrections applied after detection."""

    name: str
    params: dict = field(default_factory=dict)
    # Raw indices (as printed on the raw preview) to discard - text labels,
    # duplicated night variants, artefacts.
    drop: list[int] = field(default_factory=list)
    # Groups of raw indices that are really one object.
    merge: list[list[int]] = field(default_factory=list)
    # {"index": 7, "axis": "x", "at": 812} splits raw box 7 at sheet x=812.
    split: list[dict] = field(default_factory=list)
    # Fully manual extra boxes, [x0, y0, x1, y1] in sheet pixels.
    boxes: list[list[int]] = field(default_factory=list)

    def param(self, key: str):
        return self.params.get(key, DEFAULTS[key])


# --------------------------------------------------------------------------
# Detection
# --------------------------------------------------------------------------

def detect_boxes(alpha: np.ndarray, cfg: SheetConfig) -> list[Box]:
    """Return one box per detected object, in raw label order."""
    core = alpha >= cfg.param("core_threshold")
    # A 3x3 closing welds the 1px seams that anti-aliasing leaves inside a body
    # without reaching across the gap between two neighbouring objects.
    core = ndimage.binary_closing(core, structure=np.ones((3, 3)))

    labels, n = ndimage.label(core)
    if n == 0:
        return []

    counts = np.bincount(labels.ravel())
    keep = np.where(counts[1:] >= cfg.param("min_core_area"))[0] + 1
    if keep.size == 0:
        return []

    # Compact the surviving labels to 1..k so downstream indexing is dense.
    remap = np.zeros(n + 1, dtype=np.int32)
    remap[keep] = np.arange(1, keep.size + 1, dtype=np.int32)
    labels = remap[labels]

    full = alpha >= cfg.param("edge_threshold")
    # Nearest-core ownership: every soft pixel joins the object it touches,
    # never the one that merely glows near it.
    dist, (iy, ix) = ndimage.distance_transform_edt(labels == 0, return_indices=True)
    owner = labels[iy, ix]
    owner = np.where(full & (dist <= cfg.param("max_glow_dist")), owner, 0)

    boxes: list[Box] = []
    for sl in ndimage.find_objects(owner, max_label=int(labels.max())):
        if sl is None:
            continue
        ys, xs = sl
        boxes.append(Box(xs.start, ys.start, xs.stop, ys.stop))
    return boxes


def merge_overlapping(boxes: list[Box], threshold: float) -> tuple[list[Box], list[list[int]]]:
    """Union-find merge of boxes that overlap more than ``threshold``.

    Returns the merged boxes and, for each, the raw indices it came from.
    """
    parent = list(range(len(boxes)))

    def find(i: int) -> int:
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    def union(i: int, j: int) -> None:
        ri, rj = find(i), find(j)
        if ri != rj:
            parent[max(ri, rj)] = min(ri, rj)

    for i in range(len(boxes)):
        for j in range(i + 1, len(boxes)):
            inter = boxes[i].intersection_area(boxes[j])
            if inter == 0:
                continue
            smaller = min(boxes[i].area, boxes[j].area)
            if smaller > 0 and inter / smaller >= threshold:
                union(i, j)

    groups: dict[int, list[int]] = {}
    for i in range(len(boxes)):
        groups.setdefault(find(i), []).append(i)

    merged, provenance = [], []
    for members in groups.values():
        box = boxes[members[0]]
        for k in members[1:]:
            box = box.union(boxes[k])
        merged.append(box)
        provenance.append(sorted(members))
    return merged, provenance


def reading_order(boxes: list[Box], tolerance_factor: float) -> list[int]:
    """Indices of ``boxes`` sorted top-to-bottom by row, then left-to-right."""
    if not boxes:
        return []
    tol = max(24.0, float(np.median([b.h for b in boxes])) * tolerance_factor)

    order = sorted(range(len(boxes)), key=lambda i: boxes[i].cy)
    rows: list[list[int]] = []
    for i in order:
        if rows and abs(boxes[i].cy - boxes[rows[-1][0]].cy) <= tol:
            rows[-1].append(i)
        else:
            rows.append([i])

    flat: list[int] = []
    for row in rows:
        flat.extend(sorted(row, key=lambda i: boxes[i].x0))
    return flat


# --------------------------------------------------------------------------
# Manual corrections
# --------------------------------------------------------------------------

def apply_corrections(boxes: list[Box], cfg: SheetConfig) -> list[Box]:
    """Apply drop / merge / split / manual boxes, in that order."""
    alive = {i: b for i, b in enumerate(boxes) if i not in set(cfg.drop)}

    for group in cfg.merge:
        members = [i for i in group if i in alive]
        if len(members) < 2:
            print(f"  ! merge group {group} has <2 live members - skipped", file=sys.stderr)
            continue
        box = alive[members[0]]
        for i in members[1:]:
            box = box.union(alive.pop(i))
        alive[members[0]] = box

    for spec in cfg.split:
        idx = spec["index"]
        if idx not in alive:
            print(f"  ! split index {idx} is not live - skipped", file=sys.stderr)
            continue
        box = alive.pop(idx)
        at, axis = int(spec["at"]), spec.get("axis", "x")
        if axis == "x":
            left, right = Box(box.x0, box.y0, at, box.y1), Box(at, box.y0, box.x1, box.y1)
        else:
            left, right = Box(box.x0, box.y0, box.x1, at), Box(box.x0, at, box.x1, box.y1)
        alive[idx] = left
        alive[max(alive) + 1_000 + idx] = right

    result = list(alive.values())
    result.extend(Box(*b) for b in cfg.boxes)
    return result


# --------------------------------------------------------------------------
# Export
# --------------------------------------------------------------------------

def crop_and_trim(rgba: np.ndarray, box: Box, cfg: SheetConfig) -> Image.Image | None:
    """Crop ``box`` (plus padding), flatten the haze, then trim to tight alpha."""
    h, w, _ = rgba.shape
    pad = cfg.param("pad")
    x0, y0 = max(0, box.x0 - pad), max(0, box.y0 - pad)
    x1, y1 = min(w, box.x1 + pad), min(h, box.y1 + pad)

    patch = rgba[y0:y1, x0:x1].copy()
    cutoff = cfg.param("haze_cutoff")
    patch[..., 3] = np.where(patch[..., 3] < cutoff, 0, patch[..., 3])
    # A fully transparent pixel keeps whatever RGB the generator left there;
    # zeroing it stops the atlas packer's alpha dilation from smearing it.
    patch[..., :3] = np.where(patch[..., 3:4] == 0, 0, patch[..., :3])

    patch = drop_specks(patch, cfg)

    ys, xs = np.nonzero(patch[..., 3])
    if ys.size == 0:
        return None
    patch = patch[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
    return Image.fromarray(patch, "RGBA")


def drop_specks(patch: np.ndarray, cfg: SheetConfig) -> np.ndarray:
    """Erase tiny disconnected islands left by a neighbour's stray pixels.

    A prop legitimately made of several parts (two sacks, a bucket beside a
    trough, a cluster of mushrooms) keeps them: an island only dies when it is
    both absolutely small and negligible against the object's own mass.
    """
    solid = patch[..., 3] >= cfg.param("core_threshold")
    labels, n = ndimage.label(solid, structure=np.ones((3, 3)))
    if n <= 1:
        return patch

    counts = np.bincount(labels.ravel())[1:]
    biggest = counts.max()
    doomed = np.where((counts < cfg.param("speck_area")) &
                      (counts < biggest * cfg.param("speck_fraction")))[0] + 1
    if doomed.size == 0:
        return patch

    # Grow the kill mask so each speck's own soft halo goes with it.
    kill = ndimage.binary_dilation(np.isin(labels, doomed), structure=np.ones((9, 9)))
    kill &= ~ndimage.binary_dilation(np.isin(labels, np.setdiff1d(np.arange(1, n + 1), doomed)),
                                     structure=np.ones((5, 5)))
    patch[..., 3] = np.where(kill, 0, patch[..., 3])
    patch[..., :3] = np.where(patch[..., 3:4] == 0, 0, patch[..., :3])
    return patch


def audit_coverage(alpha: np.ndarray, boxes: list[Box], cfg: SheetConfig,
                   min_cluster: int = 200) -> list[Box]:
    """Report solid mass that no box covers.

    An object made of several small disconnected parts (a clover sprig, a spray
    of petals) falls under ``min_core_area`` component by component and is
    silently lost.  This is the check that makes that loud instead of silent.
    """
    covered = np.zeros(alpha.shape, dtype=bool)
    for b in boxes:
        covered[b.y0:b.y1, b.x0:b.x1] = True

    orphan = (alpha >= cfg.param("core_threshold")) & ~covered
    if not orphan.any():
        return []

    # Dilate so the scattered parts of one object cluster into one report entry.
    grouped = ndimage.binary_dilation(orphan, structure=np.ones((15, 15)))
    labels, n = ndimage.label(grouped)
    counts = ndimage.sum(orphan, labels, range(1, n + 1))

    missed = []
    for i, sl in enumerate(ndimage.find_objects(labels)):
        if sl is None or counts[i] < min_cluster:
            continue
        ys, xs = sl
        missed.append(Box(xs.start, ys.start, xs.stop, ys.stop))
    return missed


def write_preview(rgba: np.ndarray, boxes: list[Box], order: list[int], out_path: str) -> None:
    """Contact preview of the sheet over magenta with every box numbered."""
    flat = rgba.copy()
    flat[..., 3] = np.where(flat[..., 3] < DEFAULTS["haze_cutoff"], 0, flat[..., 3])
    base = Image.new("RGBA", (flat.shape[1], flat.shape[0]), (255, 0, 255, 255))
    im = Image.alpha_composite(base, Image.fromarray(flat, "RGBA"))

    draw = ImageDraw.Draw(im)
    for slot, idx in enumerate(order):
        b = boxes[idx]
        draw.rectangle([b.x0, b.y0, b.x1, b.y1], outline=(0, 255, 0, 255), width=3)
        label = str(slot)
        tx, ty = b.x0 + 3, b.y0 + 3
        draw.rectangle([tx - 2, ty - 2, tx + 9 * len(label) + 4, ty + 20], fill=(0, 0, 0, 255))
        draw.text((tx, ty), label, fill=(255, 255, 0, 255))
    im.convert("RGB").save(out_path)


def slice_sheet(sheet_path: str, out_dir: str, cfg: SheetConfig, raw_preview: bool) -> dict:
    rgba = np.array(Image.open(sheet_path).convert("RGBA"))
    alpha = rgba[..., 3]

    raw = detect_boxes(alpha, cfg)
    raw, _ = merge_overlapping(raw, cfg.param("merge_overlap"))
    raw = [raw[i] for i in reading_order(raw, cfg.param("row_tolerance"))]

    os.makedirs(out_dir, exist_ok=True)
    stem = os.path.splitext(os.path.basename(sheet_path))[0]

    if raw_preview:
        # Raw indices are what the drop / merge / split config refers to.
        write_preview(rgba, raw, list(range(len(raw))), os.path.join(out_dir, f"{stem}__raw.png"))
        print(f"{stem}: {len(raw)} raw boxes -> {stem}__raw.png")
        for m in audit_coverage(alpha, raw, cfg):
            print(f"  ! uncovered mass at {m.as_list()} - add it to 'boxes'", file=sys.stderr)
        return {"sheet": stem, "raw_count": len(raw)}

    boxes = apply_corrections(raw, cfg)
    order = reading_order(boxes, cfg.param("row_tolerance"))
    boxes = [boxes[i] for i in order]

    crops_dir = os.path.join(out_dir, stem)
    os.makedirs(crops_dir, exist_ok=True)
    entries = []
    for slot, box in enumerate(boxes):
        img = crop_and_trim(rgba, box, cfg)
        if img is None:
            print(f"  ! box {slot} is empty after trim - skipped", file=sys.stderr)
            continue
        rel = f"{stem}_{slot:03d}.png"
        img.save(os.path.join(crops_dir, rel))
        entries.append({
            "index": slot,
            "file": rel,
            "sheet_box": box.as_list(),
            "size": [img.width, img.height],
        })

    write_preview(rgba, boxes, list(range(len(boxes))), os.path.join(out_dir, f"{stem}__final.png"))

    uncovered = [m.as_list() for m in audit_coverage(alpha, boxes, cfg)]
    for m in uncovered:
        print(f"  ! uncovered mass at {m} - add it to 'boxes'", file=sys.stderr)

    manifest = {"sheet": stem, "source": sheet_path.replace("\\", "/"), "count": len(entries),
                "uncovered": uncovered,
                "params": {k: cfg.param(k) for k in DEFAULTS}, "items": entries}
    with open(os.path.join(out_dir, f"{stem}.slices.json"), "w", encoding="utf-8") as fh:
        json.dump(manifest, fh, indent=2)
    print(f"{stem}: {len(entries)} crops -> {crops_dir}")
    return manifest


# --------------------------------------------------------------------------
# Entry point
# --------------------------------------------------------------------------

def load_configs(path: str | None) -> dict[str, SheetConfig]:
    if not path or not os.path.exists(path):
        return {}
    with open(path, encoding="utf-8") as fh:
        raw = json.load(fh)
    fields = {"params", "drop", "merge", "split", "boxes"}
    return {name: SheetConfig(name=name, **{k: v for k, v in spec.items() if k in fields})
            for name, spec in raw.items()}


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--sheet", help="Path to one sheet PNG")
    ap.add_argument("--sheet-dir", help="Directory of sheet PNGs (with --all)")
    ap.add_argument("--all", action="store_true", help="Process every PNG in --sheet-dir")
    ap.add_argument("--out", required=True, help="Output directory")
    ap.add_argument("--config", help="JSON of per-sheet corrections")
    ap.add_argument("--raw-preview", action="store_true",
                    help="Only write the numbered raw preview (indices for the config)")
    args = ap.parse_args()

    configs = load_configs(args.config)

    if args.all:
        if not args.sheet_dir:
            ap.error("--all requires --sheet-dir")
        sheets = sorted(os.path.join(args.sheet_dir, f)
                        for f in os.listdir(args.sheet_dir) if f.lower().endswith(".png"))
    elif args.sheet:
        sheets = [args.sheet]
    else:
        ap.error("pass --sheet or --all")

    for sheet in sheets:
        stem = os.path.splitext(os.path.basename(sheet))[0]
        cfg = configs.get(stem, SheetConfig(name=stem))
        slice_sheet(sheet, args.out, cfg, args.raw_preview)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
