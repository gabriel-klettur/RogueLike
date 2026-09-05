#!/usr/bin/env python3
"""Cut a spell-icon sheet into one named, trimmed PNG per spell.

WHY NOT BAND PROJECTION
-----------------------
``minerals/build_mineral_icons.py`` cuts its sheet by projecting the alpha onto
each axis and reading the gaps between bands. That is the right tool for a sheet
whose cells do not touch, and it is useless here: every icon on these sheets
carries a wide soft GLOW, and on the wave-7 sheet rows 1-3 bleed into each other
hard enough that a horizontal projection reports *one* band 645 px tall covering
three rows. Measured, the only gap the projection finds is above row 4.

So the segmentation runs the other way round. A high alpha threshold isolates the
solid CORES (the glow is faint, the core is not), those cores are clustered onto
the declared grid, and every remaining glow pixel is then handed to whichever core
is nearest. That last step is the one that matters: where two glows overlap the
boundary lands in the dim valley between them, which is where a human would cut it
too.

WHY A TABLE RATHER THAN A CLASSIFIER
------------------------------------
Same reason ``wave2/classify.py`` and the minerals sheet give: what a painted
glyph MEANS cannot be read off its pixels. Hue says a cell is violet and does not
say whether that violet is a void lance, a curse or a raised thrall -- the wave-7
sheet holds all three side by side in the same violet. Every row below is declared
by hand against the rendered sheet and the tool only segments, trims, places and
names.

SIZING
------
Icons are written at NATIVE resolution, trimmed to their own alpha and centred on
a square canvas. No upscaling. The shipped icons under ``Art/UI/spells`` are
1024 px and these sheets' cells are 100-365 px, so matching them would mean
interpolating a three- to tenfold blow-up of every icon and paying for it in
``ui.spriteatlas`` -- for a HUD slot that draws them at 40 px.
``minerals/build_mineral_icons.py`` made the same call for the same reason.

Nothing is normalised to fill the canvas either: the shipped set runs from 0.84
wide by 0.15 tall (``laser_beam``) to 0.43 square (``healing_aura``), so a beam
that reads as a beam BECAUSE it is long and thin is left long and thin.

USAGE
-----
    python tools/atlas/wave7/build_spell_icons.py --dry-run --contact-sheet
    python tools/atlas/wave7/build_spell_icons.py
    python tools/atlas/wave7/build_spell_icons.py --only wave8
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage as ndi

REPO = Path(__file__).resolve().parents[3]
ART_OUT = REPO / "unity/Valkur/Assets/_Project/Art/UI/spells"
GENERATED = REPO / "tools/atlas/generated"

#: Alpha at or above which a pixel counts as part of an icon at all. Both sheets'
#: alpha is strongly bimodal, so this floor keeps the full soft halo without
#: picking up the compositor's noise. The wave-8 sheet never reaches a true zero
#: (only 0.2% of it is alpha 0, against 31% of wave 7) because its background was
#: knocked out imperfectly -- but composited onto mid-grey, floors of 8 and 48
#: are indistinguishable, so the residue is faint enough to ignore.
MASK_ALPHA = 8

#: Alpha at or above which a pixel is CORE rather than glow, used to seed the
#: segmentation. Measured on both sheets across 8..200: below ~48 the low-alpha
#: haze bridges neighbouring icons and the blob count collapses (wave 8 finds 6
#: icons at a threshold of 8, and 16 at anything from 48 up). 64 sits inside the
#: stable plateau for both.
SEED_ALPHA = 64

#: Seed fragments smaller than this are compositor speckle, not an icon.
MIN_SEED_PIXELS = 1200

#: Alpha used to compute the CROP box. Trimming on MASK_ALPHA lets a single stray
#: pixel drag the box out and shrink the icon inside its canvas; the written PNG
#: still carries every pixel above MASK_ALPHA. Measured on wave 8, the box grows
#: only ~10 px between a trim of 64 and one of 24, so nothing is running away.
TRIM_ALPHA = 24

#: Two icons whose centroids are closer than this on an axis are the same cell.
#: Real columns sit 225-360 px apart; the one icon that segments into two
#: fragments (wave 7's scatter_volley) has them 14 px apart.
CELL_GAP = 110

# (row, col) -> (spellKey, display name, category folder)
#
# Declared by hand against each rendered sheet and cross-checked against the
# catalog: every key resolves to Data/Catalogs/Spells/<key>.asset. The category is
# the spell's own SpellType, which is why wave 7's two lances and its curse sit
# under projectiles/ -- the folder is organizational only (the auto-assigner
# matches by filename anywhere under Art/UI/spells), but filing it by type keeps
# it honest.

WAVE7 = [
    # row 0 -- ice, then the first two nature glyphs
    (0, 0, "frost_nova",       "Frost Nova",       "area"),
    (0, 1, "ice_lance",        "Ice Lance",        "projectiles"),
    (0, 2, "glacial_step",     "Glacial Step",     "mobility"),
    (0, 3, "frozen_ward",      "Frozen Ward",      "defense"),
    (0, 4, "blizzard",         "Blizzard",         "area"),
    (0, 5, "thorn_burst",      "Thorn Burst",      "area"),
    (0, 6, "entangle",         "Entangle",         "area"),

    # row 1 -- nature, then shadow
    (1, 0, "barkskin",         "Barkskin",         "defense"),
    (1, 1, "spore_cloud",      "Spore Cloud",      "area"),
    (1, 2, "summon_wolf",      "Summon Wolf",      "summoning"),
    (1, 3, "shadow_step",      "Shadow Step",      "mobility"),
    (1, 4, "void_lance",       "Void Lance",       "projectiles"),
    (1, 5, "curse_of_frailty", "Curse of Frailty", "projectiles"),
    (1, 6, "raise_thrall",     "Raise Thrall",     "projectiles"),

    # row 2 -- holy, then storm
    (2, 0, "radiant_burst",    "Radiant Burst",    "area"),
    (2, 1, "blessing",         "Blessing",         "defense"),
    (2, 2, "sanctuary",        "Sanctuary",        "defense"),
    (2, 3, "guardian_light",   "Guardian Light",   "defense"),
    (2, 4, "seeking_shard",    "Seeking Shard",    "projectiles"),
    (2, 5, "thunderclap",      "Thunderclap",      "area"),
    (2, 6, "static_field",     "Static Field",     "area"),

    # row 3 -- martial and fire; the sheet's 28th cell is empty
    (3, 0, "scatter_volley",   "Scatter Volley",   "projectiles"),
    (3, 1, "war_cry",          "War Cry",          "utility"),
    (3, 2, "leap_slam",        "Leap Slam",        "mobility"),
    (3, 3, "charged_bolt",     "Charged Bolt",     "projectiles"),
    (3, 4, "cinder_trail",     "Cinder Trail",     "area"),
    (3, 5, "arcane_barrier",   "Arcane Barrier",   "area"),
]

# Wave 8 retires the last of the shared icons. Six of these spells were drawing
# laser_beam.png, one was drawing slash.png, and the rest had no icon at all --
# see the "Icons that were shared" section of the folder README.
WAVE8 = [
    # row 0 -- the laser family, which differs ONLY by colour and should read as
    # a family; the four saturated hues
    (0, 0, "laser_beam_red",    "Laser Red",       "projectiles"),
    (0, 1, "laser_beam_blue",   "Laser Blue",      "projectiles"),
    (0, 2, "laser_beam_green",  "Laser Green",     "projectiles"),
    (0, 3, "laser_beam_yellow", "Laser Yellow",    "projectiles"),

    # row 1 -- the two hard lasers, then the two blades
    (1, 0, "laser_beam_white",  "Laser White",     "projectiles"),
    (1, 1, "laser_beam_black",  "Laser Void",      "projectiles"),
    (1, 2, "slash_regular",     "Slash Regular",   "melee"),
    (1, 3, "weapon_toggle",     "Draw Weapon",     "utility"),

    # rows 2-3 -- the ki intensity ladder. SpellDefinition.scale runs 0.15 to 1.00
    # across these seven and moves DENSITY, not size, so the art keeps one height
    # and escalates in violence.
    (2, 0, "charge_ki_spirit",  "Ki Charge: Spirit",  "charges"),
    (2, 1, "charge_ki_azure",   "Ki Charge: Azure",   "charges"),
    (2, 2, "charge_ki_verdant", "Ki Charge: Verdant", "charges"),
    (2, 3, "charge_ki_solar",   "Ki Charge: Solar",   "charges"),
    (3, 0, "charge_ki_crimson", "Ki Charge: Crimson", "charges"),
    (3, 1, "charge_ki_violet",  "Ki Charge: Violet",  "charges"),
    (3, 2, "charge_ki_void",    "Ki Charge: Void",    "charges"),

    (3, 3, "summon_barbol",     "Summon Barbol",   "summoning"),
]

#: Each sheet: the source PNG, its square canvas, the cells it must segment into,
#: and its table. The sheets live in ``staging/``, not under ``Assets/``: Unity
#: imports everything under Assets whether or not it is referenced, and
#: ``Art/UI/spells`` is packed whole by ``ui.spriteatlas`` -- so leaving a sheet
#: beside the icons cut FROM it ships both, and pays for the sheet twice.
SHEETS = [
    {
        "name": "wave7",
        "sheet": REPO / "staging/spells/last_spells_added.png",
        "canvas": 320,
        "expected_cols": {0: 7, 1: 7, 2: 7, 3: 6},
        "table": WAVE7,
    },
    {
        "name": "wave8",
        "sheet": REPO / "staging/spells/spells_without_icons.png",
        "canvas": 384,
        "expected_cols": {0: 4, 1: 4, 2: 4, 3: 4},
        "table": WAVE8,
    },
]


def cluster(values, gap):
    """Group values into clusters split on any gap wider than ``gap``.

    Returns a cluster index per input position, numbered in ascending value
    order. Used on both axes: rows split on the vertical gaps between core
    centroids, columns on the horizontal ones.
    """
    order = sorted(range(len(values)), key=lambda i: values[i])
    out = [0] * len(values)
    group = 0
    for n, i in enumerate(order):
        if n and values[i] - values[order[n - 1]] > gap:
            group += 1
        out[i] = group
    return out


def segment(sheet, expected_cols):
    """Label every pixel of the sheet with the icon it belongs to.

    Returns the label image (0 = background) and a (row, col) -> label map.
    """
    alpha = np.array(sheet)[:, :, 3]
    mask = alpha >= MASK_ALPHA

    seeds, n = ndi.label(alpha >= SEED_ALPHA)
    sizes = np.bincount(seeds.ravel())
    sizes[0] = 0
    kept = [i for i in range(1, n + 1) if sizes[i] >= MIN_SEED_PIXELS]
    if not kept:
        raise SystemExit("no seeds survived MIN_SEED_PIXELS -- wrong sheet?")

    centres = [ndi.center_of_mass(seeds == i) for i in kept]
    rows = cluster([c[0] for c in centres], CELL_GAP)

    # Columns are clustered WITHIN a row: clustering every x-centroid at once
    # would fuse several rows' worth of columns and hide a missing icon.
    cell_of_seed = {}
    for r in sorted(set(rows)):
        members = [k for k in range(len(kept)) if rows[k] == r]
        cols = cluster([centres[k][1] for k in members], CELL_GAP)
        for k, c in zip(members, cols):
            cell_of_seed[kept[k]] = (r, c)

    found = {}
    for r, c in cell_of_seed.values():
        found.setdefault(r, set()).add(c)
    got = {r: len(cs) for r, cs in sorted(found.items())}
    if got != expected_cols:
        raise SystemExit(f"grid mismatch: expected {expected_cols}, segmented {got}")

    # Fold the seed fragments of a cell into one label, then hand every remaining
    # glow pixel to the nearest seed. distance_transform_edt's return_indices
    # gives, for each pixel, the coordinates of the closest seed pixel -- reading
    # the label there is a Voronoi partition, restricted to the mask.
    cell_label = {cell: i + 1 for i, cell in enumerate(sorted(set(cell_of_seed.values())))}
    merged = np.zeros_like(seeds)
    for seed_id, cell in cell_of_seed.items():
        merged[seeds == seed_id] = cell_label[cell]

    _, (iy, ix) = ndi.distance_transform_edt(merged == 0, return_indices=True)
    labels = np.where(mask, merged[iy, ix], 0)
    return labels, cell_label


def bleed_rgb(rgba):
    """Flood each transparent pixel's RGB with its nearest opaque pixel's colour.

    A fully transparent pixel still carries an RGB triple, and any filtering that
    averages neighbours -- mip generation, atlas downscale, the bilinear sample
    that draws a 384 px icon into a 40 px HUD slot -- mixes it back in. The wave-8
    sheet's knocked-out background is not black but mottled colour (mean RGB
    97,87,98 over the 10% of it sitting at alpha 9-31), so leaving it in place
    fringes every icon with whatever junk happened to surround it on the sheet.

    Zeroing it instead is the other classic mistake and rings the icon with a dark
    halo; that is the same failure ``build_building_props.py`` and the minerals
    cutter avoid by compositing in premultiplied alpha.
    """
    opaque = rgba[:, :, 3] > 0
    if not opaque.any():
        return rgba
    _, (iy, ix) = ndi.distance_transform_edt(~opaque, return_indices=True)
    out = rgba.copy()
    out[:, :, :3] = rgba[iy, ix, :3]
    out[:, :, 3] = rgba[:, :, 3]
    return out


def cut_sheet(spec, dry_run, contact_sheet):
    sheet = Image.open(spec["sheet"]).convert("RGBA")
    labels, cell_label = segment(sheet, spec["expected_cols"])
    pixels = np.array(sheet)
    canvas_px = spec["canvas"]

    declared = {(r, c) for r, c, *_ in spec["table"]}
    if declared != set(cell_label):
        missing = sorted(declared - set(cell_label))
        extra = sorted(set(cell_label) - declared)
        raise SystemExit(f"{spec['name']}: table/sheet disagree -- missing {missing}, extra {extra}")

    records, tiles = [], []
    for row, col, key, display, category in spec["table"]:
        region = labels == cell_label[(row, col)]
        keep = region & (pixels[:, :, 3] >= TRIM_ALPHA)
        ys, xs = np.where(keep)
        y0, y1 = int(ys.min()), int(ys.max()) + 1
        x0, x1 = int(xs.min()), int(xs.max()) + 1

        if (x1 - x0) > canvas_px or (y1 - y0) > canvas_px:
            raise SystemExit(f"{key}: {x1 - x0}x{y1 - y0} exceeds canvas {canvas_px}")

        # Mask out the neighbours' glow that falls inside this icon's box before
        # cropping, or a wide icon carries a slice of whatever sits beside it.
        cropped = pixels[y0:y1, x0:x1].copy()
        cropped[:, :, 3] *= region[y0:y1, x0:x1]
        cell = Image.fromarray(bleed_rgb(cropped), "RGBA")

        canvas = Image.new("RGBA", (canvas_px, canvas_px), (0, 0, 0, 0))
        canvas.alpha_composite(cell, ((canvas_px - cell.width) // 2,
                                      (canvas_px - cell.height) // 2))

        out = ART_OUT / category / f"{key}.png"
        records.append({
            "spellKey": key,
            "displayName": display,
            "category": category,
            "path": str(out.relative_to(REPO)).replace("\\", "/"),
            "row": row,
            "col": col,
            "sourceBox": [x0, y0, x1, y1],
            "trimmed": [cell.width, cell.height],
            "canvas": canvas_px,
        })
        tiles.append(canvas)
        print(f"  {key:<18} {cell.width:>3}x{cell.height:<3} -> {category}/{key}.png")

        if not dry_run:
            out.parent.mkdir(parents=True, exist_ok=True)
            canvas.save(out)

    if contact_sheet:
        cols = spec["expected_cols"][0]
        rows = (len(tiles) + cols - 1) // cols
        img = Image.new("RGBA", (cols * canvas_px, rows * canvas_px), (18, 18, 24, 255))
        for i, tile in enumerate(tiles):
            img.alpha_composite(tile, ((i % cols) * canvas_px, (i // cols) * canvas_px))
        GENERATED.mkdir(parents=True, exist_ok=True)
        path = GENERATED / f"spell_icons_{spec['name']}_contact.png"
        img.save(path)
        print(f"  contact sheet -> {path.relative_to(REPO)}")

    if not dry_run:
        GENERATED.mkdir(parents=True, exist_ok=True)
        manifest = GENERATED / f"spell_icons_manifest_{spec['name']}.json"
        manifest.write_text(json.dumps({
            "source": str(spec["sheet"].relative_to(REPO)).replace("\\", "/"),
            "canvas": canvas_px,
            "maskAlpha": MASK_ALPHA,
            "seedAlpha": SEED_ALPHA,
            "trimAlpha": TRIM_ALPHA,
            "icons": records,
        }, indent=2) + "\n", encoding="utf-8")
        print(f"  manifest -> {manifest.relative_to(REPO)}")

    return len(records)


def main():
    ap = argparse.ArgumentParser(description="Cut the spell-icon sheets.")
    ap.add_argument("--dry-run", action="store_true", help="segment and report, write nothing")
    ap.add_argument("--contact-sheet", action="store_true", help="also write a contact sheet")
    ap.add_argument("--only", metavar="NAME", help="cut just this sheet (e.g. wave8)")
    args = ap.parse_args()

    total = 0
    for spec in SHEETS:
        if args.only and spec["name"] != args.only:
            continue
        print(f"{spec['name']}  ({spec['sheet'].name})")
        total += cut_sheet(spec, args.dry_run, args.contact_sheet)
        print()
    if total == 0:
        raise SystemExit(f"no sheet matched --only {args.only!r}")
    print(f"{total} icons {'planned' if args.dry_run else 'written'}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
