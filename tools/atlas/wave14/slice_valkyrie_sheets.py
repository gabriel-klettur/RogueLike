#!/usr/bin/env python3
"""Cut the valkyrie wave14 sheets into one cleaned patch per frame.

Why this wave needs its own slicer
----------------------------------
``slice_prop_sheet.py`` segments on the alpha channel: solid cores first, then every
soft pixel joins the core it is NEAREST. That is exactly right for a prop sheet and
for every player wave so far, because those characters' poses never touch. This one's
do. The valkyrie is drawn holding a two-handed greatsword longer than a grid cell, so
the blade of one pose runs THROUGH the body of the next and the two become a single
core. Measured over the forty-five staged sheets, twelve came back short:
``shield_attack`` returned five items for eight frames, with one 1150px box holding
four poses at once, and ``knockdown_recovery`` returned five for eight.

Two sheets fail the other way. ``spellcast_3``'s golden rune FX is drawn detached from
the hand that conjures it, so it segments as its own object and the sheet came back
with ten items for eight frames -- the same shape as ``elf_archer_cast``'s summoned bow,
which wave5 fixed with a hand-written ``merge`` in its config.

Both directions could be corrected by hand from a config, the way wave5 and wave2 did:
forty-odd raw indices, re-derived every time the art is restaged. What makes that
unnecessary here is that these sheets ARE on an even grid. The number of frames in a
row is known, so it can be used as a CHECK -- wave13's slicer takes the same position
and for the same reason -- and the correction becomes arithmetic instead of a table:
a box about k cells wide holds k poses and gets k-1 cuts; a fragment too small to be a
pose belongs to whichever pose is nearest.

How a merged box is cut
-----------------------
At the column of LEAST SOLID INK inside a window around each nominal cell boundary.
Choosing the minimum is what keeps the damage to a pixel-column of blade rather than to
a body: the bodies sit at the cell centres and the overlap happens between them, so the
thinnest place in the box really is where one pose ends and the next begins. The window
is +/-30% of a cell, so a pose that has translated (which they do -- the builder prints
a note for each one) is still cut on its own boundary rather than on the arithmetic
midpoint.

A hard vertical cut does lose the tip of a blade that crosses the seam. That is not a
regression: ``build_player_frames.own_object_only`` would have dropped exactly that ink
anyway, as "a piece of a body that continues past the box". The difference is that it
drops it as a whole component and would take the pose's OWN sword with it whenever the
sword is the thing bridging two frames.

Why it writes patches instead of boxes
--------------------------------------
``build_state`` re-reads the SOURCE SHEET through each item's ``sheet_box``, so any
cleaning done here would be thrown away -- and on these sheets the boxes overlap by
more than half a cell, which means the neighbour inside a box is sometimes the LARGEST
core in it. ``own_object_only`` keeps the largest core by construction, so it would keep
the wrong character. This tool therefore resolves ownership itself and writes, per frame,
a patch of EXACTLY ``sheet_box`` size with the foreign ink erased; ``build_state`` uses
it when the manifest declares one.

Not trimmed, which is the whole objection ``build_player_frames``' docstring raises
against reaching for a slicer's crops: trimming each frame to its own alpha is what makes
a walk jitter. A patch here is the box, in sheet coordinates, with pixels removed.

Ownership inside a box is decided the way ``slice_prop_sheet`` decides it -- every ink
pixel joins the nearest group by Euclidean distance -- with the groups being the
post-split poses rather than raw cores.

Usage
-----
    python tools/atlas/wave14/slice_valkyrie_sheets.py [--only <stem> ...] [--dry-run]

Then feed the output directory to the builder:

    python tools/atlas/wave3/build_player_frames.py staging/players/valkyrie_wave14_slices --only valkyrie
"""

from __future__ import annotations

import argparse
import json
import os

import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
STAGING = os.path.join(REPO, "staging", "players", "valkyrie_wave14")
OUT_ROOT = os.path.join(REPO, "staging", "players", "valkyrie_wave14_slices")

# Same two thresholds slice_prop_sheet segments on, so "solid body" and "keep this soft
# edge" mean the same thing in both tools.
ALPHA_SOLID = 190
ALPHA_KEEP = 16
# A core smaller than this is a speck, not a pose and not a piece of one worth keeping
# as its own group. Lower than slice_prop_sheet's 700 because a detached FX swirl is a
# legitimate fragment here and gets folded into its pose rather than dropped.
MIN_CORE_AREA = 220
# A group whose core is smaller than this fraction of the row's median core cannot be a
# pose; it is FX, a dropped weapon or a torn cape tip, and belongs to its nearest pose.
FRAGMENT_FRACTION = 0.22
# Half-width of the search window for a seam, as a fraction of a cell. A pose translates
# by well under a third of a cell in this wave (the builder prints one note per frame
# that leaves its own cell), so this is wide enough to find the real gap and narrow
# enough that it cannot wander onto the next boundary.
SEAM_WINDOW = 0.30
# Rows are clustered by the gaps between item centres; a gap this many times the median
# starts a new row. Same rule and same value as slice_prop_sheet's row_tolerance.
ROW_GAP_FACTOR = 0.55

# Frames per row, declared per sheet, checked rather than trusted.
#
# It is a CHECK and not the source of truth: the tool finds the poses itself and uses
# this number to decide whether what it found is right, exactly as wave13's slicer uses
# its own counts. A sheet that disagrees stops the run and names itself, because the two
# ways it can disagree are both silent otherwise -- a merged pair ships as one frame with
# two characters in it, and a split pose ships as half a character.
#
# Counted by eye at full resolution off the contact sheets. Anything absent is 8x1, which
# is what forty of the forty-five are.
GRID: dict[str, tuple[int, int]] = {
    "valkyrie_hit_reaction":    (4, 1),
    "valkyrie_idle":            (6, 1),
    "valkyrie_punch":           (6, 1),
    "valkyrie_shield_idle":     (6, 1),
    "valkyrie_greatsword_idle": (6, 1),
    # Genuinely one animation laid out 4x2 -- both of these read across the rows, which
    # is why they were NOT split into two sheets the way the equip combos were.
    "valkyrie_die":             (4, 2),
    "valkyrie_spellcast_3":     (4, 2),
}
DEFAULT_GRID = (8, 1)


def cores(alpha: np.ndarray) -> tuple[np.ndarray, int]:
    """Labelled solid components, specks removed."""
    solid = alpha >= ALPHA_SOLID
    # The same 3x3 closing slice_prop_sheet applies: it welds the 1px seams
    # anti-aliasing leaves inside one body without reaching between two objects.
    solid = ndimage.binary_closing(solid, structure=np.ones((3, 3)))
    labels, count = ndimage.label(solid, structure=np.ones((3, 3)))
    if count == 0:
        return labels, 0
    sizes = np.bincount(labels.ravel())
    drop = np.nonzero(sizes < MIN_CORE_AREA)[0]
    if drop.size:
        labels[np.isin(labels, drop)] = 0
    remap = {old: new for new, old in enumerate(sorted(set(np.unique(labels)) - {0}), 1)}
    out = np.zeros_like(labels)
    for old, new in remap.items():
        out[labels == old] = new
    return out, len(remap)


def cluster_rows(centres: list[float], rows_expected: int) -> list[int]:
    """Row index per item, from the vertical gaps between their centres."""
    if rows_expected == 1:
        return [0] * len(centres)
    order = sorted(range(len(centres)), key=lambda i: centres[i])
    gaps = [centres[order[i + 1]] - centres[order[i]] for i in range(len(order) - 1)]
    if not gaps:
        return [0] * len(centres)
    # The row break is the largest gap; with two rows there is exactly one of them, and
    # taking the largest is more robust than a threshold on a sheet whose poses bob.
    breaks = sorted(range(len(gaps)), key=lambda i: gaps[i], reverse=True)[:rows_expected - 1]
    breaks = set(breaks)
    row_of = {}
    row = 0
    for position, item in enumerate(order):
        row_of[item] = row
        if position in breaks:
            row += 1
    return [row_of[i] for i in range(len(centres))]


def seam_columns(solid: np.ndarray, box: tuple[int, int, int, int],
                 cuts: int, cell_w: float) -> list[int]:
    """The `cuts` sheet-x columns to split a merged box at."""
    x0, y0, x1, y1 = box
    profile = solid[y0:y1, x0:x1].sum(axis=0).astype(float)
    width = x1 - x0
    seams = []
    for k in range(1, cuts + 1):
        nominal = x0 + width * k / (cuts + 1)
        half = max(6, int(cell_w * SEAM_WINDOW))
        lo = max(x0 + 2, int(nominal) - half) - x0
        hi = min(x1 - 2, int(nominal) + half) - x0
        if hi <= lo:
            seams.append(int(nominal))
            continue
        window = profile[lo:hi]
        # Ties go to the column NEAREST the nominal boundary: a flat valley several
        # columns wide is the gap between two poses, and its middle is the honest cut.
        best = np.flatnonzero(window == window.min())
        pick = best[np.argmin(np.abs(best + lo - (nominal - x0)))]
        seams.append(int(x0 + lo + pick))
    return seams


def groups_for_row(solid: np.ndarray, items: list[dict], cols: int,
                   cell_w: float, stem: str) -> list[np.ndarray]:
    """One boolean core mask per frame, left to right, for one row of a sheet."""
    items = sorted(items, key=lambda it: it["cx"])

    # --- fold fragments into their nearest pose -----------------------------------
    if len(items) > cols:
        median = float(np.median([it["area"] for it in items]))
        poses = [it for it in items if it["area"] >= median * FRAGMENT_FRACTION]
        fragments = [it for it in items if it["area"] < median * FRAGMENT_FRACTION]
        while len(poses) > cols and fragments == []:
            # No fragment small enough to explain the surplus: the smallest item is
            # the best candidate anyway, and folding it is still better than shipping
            # a pose split in two.
            smallest = min(poses, key=lambda it: it["area"])
            poses.remove(smallest)
            fragments.append(smallest)
        for frag in fragments:
            host = min(poses, key=lambda it: abs(it["cx"] - frag["cx"]))
            host["mask"] = host["mask"] | frag["mask"]
            host["area"] += frag["area"]
        items = sorted(poses, key=lambda it: it["cx"])
        print(f"    folded {len(fragments)} fragment(s) into their nearest pose")

    if len(items) == cols:
        return [it["mask"] for it in items]

    if len(items) > cols:
        raise SystemExit(f"{stem}: {len(items)} poses in a row of {cols} and none small "
                         f"enough to fold -- check the declared grid")

    # --- split merged boxes -------------------------------------------------------
    holds = [max(1, int(round((it["x1"] - it["x0"]) / cell_w))) for it in items]
    # The widths only estimate how many poses a box holds; the declared count is what
    # decides. Hand the shortfall to the widest boxes first, which is where a merge of
    # three rather than two actually happens.
    while sum(holds) < cols:
        widest = max(range(len(items)),
                     key=lambda i: (items[i]["x1"] - items[i]["x0"]) / holds[i])
        holds[widest] += 1
    while sum(holds) > cols:
        tightest = min((i for i in range(len(items)) if holds[i] > 1),
                       key=lambda i: (items[i]["x1"] - items[i]["x0"]) / holds[i],
                       default=None)
        if tightest is None:
            raise SystemExit(f"{stem}: cannot reconcile {len(items)} boxes with {cols} "
                             f"frames -- check the declared grid")
        holds[tightest] -= 1

    masks = []
    for item, count in zip(items, holds):
        if count == 1:
            masks.append(item["mask"])
            continue
        seams = seam_columns(solid, (item["x0"], item["y0"], item["x1"], item["y1"]),
                             count - 1, cell_w)
        print(f"    split a {item['x1'] - item['x0']}px box into {count} at "
              f"x={', '.join(str(s) for s in seams)}")
        bounds = [item["x0"]] + seams + [item["x1"]]
        for k in range(count):
            piece = np.zeros_like(item["mask"])
            piece[:, bounds[k]:bounds[k + 1]] = item["mask"][:, bounds[k]:bounds[k + 1]]
            if not piece.any():
                raise SystemExit(f"{stem}: a seam left an empty frame -- the box did "
                                 f"not hold {count} poses")
            masks.append(piece)
    return masks


def slice_sheet(stem: str, dry_run: bool) -> dict | None:
    path = os.path.join(STAGING, f"{stem}.png")
    image = Image.open(path).convert("RGBA")
    pixels = np.asarray(image)
    alpha = pixels[..., 3]
    solid = alpha >= ALPHA_SOLID
    height, width = alpha.shape

    cols, rows = GRID.get(stem, DEFAULT_GRID)
    cell_w = width / cols
    labels, count = cores(alpha)
    if count == 0:
        print(f"{stem}: no solid ink at all")
        return None

    items = []
    for label in range(1, count + 1):
        mask = labels == label
        ys, xs = np.nonzero(mask)
        items.append({"mask": mask, "area": int(mask.sum()),
                      "x0": int(xs.min()), "x1": int(xs.max()) + 1,
                      "y0": int(ys.min()), "y1": int(ys.max()) + 1,
                      "cx": float(xs.mean()), "cy": float(ys.mean())})

    print(f"{stem}: {width}x{height}, grid {cols}x{rows}, {count} core(s) found")
    row_of = cluster_rows([it["cy"] for it in items], rows)
    ordered: list[np.ndarray] = []
    for row in range(rows):
        row_items = [it for it, r in zip(items, row_of) if r == row]
        if not row_items:
            raise SystemExit(f"{stem}: row {row} came back empty -- the row clustering "
                             f"disagrees with the declared grid")
        ordered.extend(groups_for_row(solid, row_items, cols, cell_w, stem))

    if len(ordered) != cols * rows:
        raise SystemExit(f"{stem}: produced {len(ordered)} frames for a "
                         f"{cols}x{rows} grid")

    # --- ownership: every soft pixel joins the nearest group ----------------------
    owner = np.zeros(alpha.shape, dtype=np.int32)
    for index, mask in enumerate(ordered, 1):
        owner[mask] = index
    keep = alpha >= ALPHA_KEEP
    _, (iy, ix) = ndimage.distance_transform_edt(owner == 0, return_indices=True)
    assigned = np.where(owner == 0, owner[iy, ix], owner)
    assigned = np.where(keep, assigned, 0)

    out_dir = os.path.join(OUT_ROOT, stem)
    if not dry_run:
        os.makedirs(out_dir, exist_ok=True)

    entries = []
    for index in range(1, len(ordered) + 1):
        mine = assigned == index
        ys, xs = np.nonzero(mine)
        x0, x1 = int(xs.min()), int(xs.max()) + 1
        y0, y1 = int(ys.min()), int(ys.max()) + 1
        patch = pixels[y0:y1, x0:x1].copy()
        patch[..., 3] = np.where(mine[y0:y1, x0:x1], patch[..., 3], 0)
        # A transparent pixel keeps whatever RGB the generator left in it; zeroing it
        # stops the atlas packer's alpha dilation from smearing a neighbour's steel back
        # over this frame's silhouette. Same reasoning as own_object_only's last line.
        patch[..., :3] = np.where(patch[..., 3:4] == 0, 0, patch[..., :3])
        name = f"{stem}_{index - 1:03d}.png"
        if not dry_run:
            Image.fromarray(patch, "RGBA").save(os.path.join(out_dir, name))
        entries.append({"index": index - 1, "file": f"{stem}/{name}",
                        "patch": f"{stem}/{name}",
                        "sheet_box": [x0, y0, x1, y1], "size": [x1 - x0, y1 - y0]})

    manifest = {
        "sheet": stem,
        "generator": "tools/atlas/wave14/slice_valkyrie_sheets.py",
        "source": os.path.relpath(path, REPO).replace("\\", "/"),
        "grid": [cols, rows],
        "count": len(entries),
        "items": entries,
    }
    if not dry_run:
        with open(os.path.join(OUT_ROOT, f"{stem}.slices.json"), "w", encoding="utf-8") as fh:
            json.dump(manifest, fh, indent=2)
            fh.write("\n")
        write_preview(stem, image, entries)
    return manifest


def write_preview(stem: str, image: Image.Image, entries: list[dict]) -> None:
    """A numbered overlay of the final boxes, so a bad cut is one look away."""
    preview = image.convert("RGB")
    draw = ImageDraw.Draw(preview)
    for entry in entries:
        x0, y0, x1, y1 = entry["sheet_box"]
        draw.rectangle([x0, y0, x1 - 1, y1 - 1], outline=(255, 220, 60), width=3)
        draw.text((x0 + 6, y0 + 6), str(entry["index"]), fill=(255, 220, 60))
    scale = min(1.0, 1600 / preview.size[0])
    if scale < 1.0:
        preview = preview.resize((int(preview.size[0] * scale),
                                  int(preview.size[1] * scale)), Image.LANCZOS)
    preview.save(os.path.join(OUT_ROOT, f"{stem}.preview.png"))


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--only", action="append", metavar="STEM",
                    help="Slice only these sheet stems (repeatable)")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    stems = sorted(f[:-4] for f in os.listdir(STAGING) if f.lower().endswith(".png"))
    if args.only:
        unknown = [s for s in args.only if s not in stems]
        if unknown:
            print(f"unknown sheet(s): {', '.join(unknown)}")
            return 2
        stems = [s for s in stems if s in args.only]

    if not args.dry_run:
        os.makedirs(OUT_ROOT, exist_ok=True)

    frames = 0
    for stem in stems:
        manifest = slice_sheet(stem, args.dry_run)
        if manifest:
            frames += manifest["count"]

    verb = "would write" if args.dry_run else "wrote"
    print(f"\n{verb} {frames} frame patches from {len(stems)} sheets into "
          f"{os.path.relpath(OUT_ROOT, REPO)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
