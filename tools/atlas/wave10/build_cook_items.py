#!/usr/bin/env python3
"""Cut the two cooking sheets into one named, trimmed PNG per item.

WHAT THESE SHEETS ARE
---------------------
Two hand-authored grids, and the split between them IS the item taxonomy:

  * ``cook_ingredients_sheet.png``  5x5  -- 25 raw INGREDIENTS. Stackable
    materials with no consume effect, so ``ItemCategoryUtil.GetCategory`` files
    them under Material without anything having to be declared.
  * ``cook_dishes_sheet.png``      6x6  -- 36 finished DISHES, six per cuisine
    (Ukraine, Chile, Spain, Iceland, Argentina, Venezuela). Each carries healing
    and hunger, so the same derivation files them under Consumable.

Nothing here stores a category: it is derived from the fields, which is why the
two tables below only have to get the ROLE right.

SEGMENTATION
------------
Same approach as ``wave7/build_spell_icons.py``, for a slightly different reason.
There the icons carried a soft glow that bled across rows; here the art is opaque
but the cells TOUCH -- measured, an alpha projection down the ingredients sheet
finds only three of the four row gaps it needs, because rows 4 and 5 run into
each other. A projection cannot recover a boundary that is not there.

So: threshold to isolate cores, cluster the cores onto the declared grid, and
hand every remaining pixel to the nearest core. Both sheets segment into exactly
their declared cell count (25 and 36) with no fragment merging needed -- each
item is a single connected blob.

NAMING
------
Declared by hand from the layout tables that shipped with the art, NOT inferred.
What a painted plate MEANS is not in its pixels: the dish sheet holds three
different fried-dough items and two different beef stews, and only the table says
which is a sopaipilla and which a churro. Same reasoning as ``wave2/classify.py``
and the minerals cutter.

The ingredient keys are what the recipe table joins on, so they are a contract:
renaming one here silently breaks every recipe naming it, which is why the
catalog test asserts that join from the other side.

USAGE
-----
    python tools/atlas/wave10/build_cook_items.py --dry-run --contact-sheet
    python tools/atlas/wave10/build_cook_items.py
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage as ndi

REPO = Path(__file__).resolve().parents[3]
ART_OUT = REPO / "unity/Valkur/Assets/_Project/Art/Items/cook"
GENERATED = REPO / "tools/atlas/generated"

#: Alpha at or above which a pixel belongs to an item at all.
MASK_ALPHA = 8

#: Alpha at or above which a pixel is CORE, used to seed the segmentation. This
#: art is opaque rather than glowing, so the stable plateau is wide and any value
#: clear of the antialiased rim works; 64 matches the spell cutter.
SEED_ALPHA = 64

#: Seed fragments smaller than this are antialiasing speckle, not an item.
#: Measured, the smallest real core is ~19,700 px and the largest rejected
#: fragment ~300, so the floor sits in a very wide valley.
MIN_SEED_PIXELS = 2000

#: Alpha used to compute the CROP box, kept above MASK_ALPHA so one stray rim
#: pixel cannot drag the box out and shrink the art inside its canvas.
TRIM_ALPHA = 24

#: Two cores closer than this on an axis are the same cell. Real columns sit
#: 240-265 px apart on both sheets and no item splits into fragments, so this
#: only guards a near miss.
CELL_GAP = 110

# (row, col) -> (itemKey, Spanish display name)
#
# Ingredients, 5x5. Read off the layout table shipped with the sheet and then
# CONFIRMED against a rendered contact sheet -- see NAMING above on why the
# pixels cannot be trusted to name themselves.
INGREDIENTS = [
    (0, 0, "beef",        "Carne de res"),
    (0, 1, "chicken",     "Pollo"),
    (0, 2, "fish",        "Pescado"),
    (0, 3, "sausage",     "Chorizo"),
    (0, 4, "egg",         "Huevo"),

    (1, 0, "potato",      "Patata"),
    (1, 1, "onion",       "Cebolla"),
    (1, 2, "garlic",      "Ajo"),
    (1, 3, "tomato",      "Tomate"),
    (1, 4, "bell_pepper", "Pimiento"),

    (2, 0, "carrot",      "Zanahoria"),
    (2, 1, "beet",        "Remolacha"),
    (2, 2, "cabbage",     "Repollo"),
    (2, 3, "corn",        "Maiz"),
    (2, 4, "avocado",     "Aguacate"),

    (3, 0, "cheese",      "Queso"),
    (3, 1, "butter",      "Mantequilla"),
    (3, 2, "sour_cream",  "Crema agria"),
    (3, 3, "rice",        "Arroz"),
    (3, 4, "black_beans", "Frijoles negros"),

    (4, 0, "flour",       "Harina"),
    (4, 1, "cornmeal",    "Harina de maiz"),
    (4, 2, "paprika",     "Pimenton"),
    (4, 3, "herbs",       "Hierbas"),
    (4, 4, "plantain",    "Platano maduro"),
]

# (row, col) -> (itemKey, Spanish display name, cuisine)
#
# Dishes, 6x6, one cuisine per ROW. The cuisine rides into the manifest and onto
# the item's description; it is not a category -- every one of these is a
# Consumable, and grouping them by nation in the crafting panel is a VIEW.
DISHES = [
    (0, 0, "borscht",               "Borscht",               "ukraine"),
    (0, 1, "varenyky",              "Varenyky",              "ukraine"),
    (0, 2, "holubtsi",              "Holubtsi",              "ukraine"),
    (0, 3, "deruny",                "Deruny",                "ukraine"),
    (0, 4, "chicken_kyiv",          "Pollo Kiev",            "ukraine"),
    (0, 5, "syrnyky",               "Syrnyky",               "ukraine"),

    (1, 0, "empanada_de_pino",      "Empanada de pino",      "chile"),
    (1, 1, "pastel_de_choclo",      "Pastel de choclo",      "chile"),
    (1, 2, "completo_chileno",      "Completo chileno",      "chile"),
    (1, 3, "cazuela_chilena",       "Cazuela chilena",       "chile"),
    (1, 4, "humitas_chilenas",      "Humitas",               "chile"),
    (1, 5, "sopaipillas_chilenas",  "Sopaipillas",           "chile"),

    (2, 0, "paella",                "Paella",                "spain"),
    (2, 1, "tortilla_espanola",     "Tortilla espanola",     "spain"),
    (2, 2, "gazpacho",              "Gazpacho",              "spain"),
    (2, 3, "croquetas",             "Croquetas",             "spain"),
    (2, 4, "jamon_iberico",         "Jamon iberico",         "spain"),
    (2, 5, "churros_con_chocolate", "Churros con chocolate", "spain"),

    (3, 0, "kjotsupa",              "Kjotsupa",              "iceland"),
    (3, 1, "plokkfiskur",           "Plokkfiskur",           "iceland"),
    (3, 2, "icelandic_pylsa",       "Pylsa islandesa",       "iceland"),
    (3, 3, "skyr",                  "Skyr",                  "iceland"),
    (3, 4, "kleinur",               "Kleinur",               "iceland"),
    (3, 5, "hakarl",                "Hakarl",                "iceland"),

    (4, 0, "asado_argentino",       "Asado argentino",       "argentina"),
    (4, 1, "empanadas_argentinas",  "Empanadas argentinas",  "argentina"),
    (4, 2, "milanesa_napolitana",   "Milanesa napolitana",   "argentina"),
    (4, 3, "choripan",              "Choripan",              "argentina"),
    (4, 4, "locro",                 "Locro",                 "argentina"),
    (4, 5, "alfajores",             "Alfajores",             "argentina"),

    (5, 0, "arepa",                 "Arepa",                 "venezuela"),
    (5, 1, "pabellon_criollo",      "Pabellon criollo",      "venezuela"),
    (5, 2, "cachapa",               "Cachapa",               "venezuela"),
    (5, 3, "tequenos",              "Tequenos",              "venezuela"),
    (5, 4, "hallaca",               "Hallaca",               "venezuela"),
    (5, 5, "asado_negro",           "Asado negro",           "venezuela"),
]

#: Art is written at NATIVE resolution and centred on a square canvas -- no
#: upscaling, and nothing is normalised to fill the canvas, so a long sausage
#: stays long. Same call the spell and mineral cutters made. 288 is the widest
#: cell either sheet produces, rounded up.
SHEETS = [
    {
        "name": "ingredients",
        "sheet": REPO / "staging/items/cook/cook_ingredients_sheet.png",
        "out": ART_OUT / "ingredients",
        "canvas": 288,
        "expected_cols": {0: 5, 1: 5, 2: 5, 3: 5, 4: 5},
        "table": [(r, c, k, n, "") for r, c, k, n in INGREDIENTS],
        "grid_cols": 5,
    },
    {
        "name": "dishes",
        "sheet": REPO / "staging/items/cook/cook_dishes_sheet.png",
        "out": ART_OUT / "dishes",
        "canvas": 288,
        "expected_cols": {0: 6, 1: 6, 2: 6, 3: 6, 4: 6, 5: 6},
        "table": DISHES,
        "grid_cols": 6,
    },
]


def cluster(values, gap):
    """Group values into clusters split on any gap wider than ``gap``."""
    order = sorted(range(len(values)), key=lambda i: values[i])
    out = [0] * len(values)
    group = 0
    for n, i in enumerate(order):
        if n and values[i] - values[order[n - 1]] > gap:
            group += 1
        out[i] = group
    return out


def segment(sheet, expected_cols):
    """Label every pixel with the item it belongs to. Returns (labels, cellmap)."""
    alpha = np.array(sheet)[:, :, 3]
    mask = alpha >= MASK_ALPHA

    seeds, n = ndi.label(alpha >= SEED_ALPHA, structure=np.ones((3, 3)))
    sizes = np.bincount(seeds.ravel())
    sizes[0] = 0
    kept = [i for i in range(1, n + 1) if sizes[i] >= MIN_SEED_PIXELS]
    if not kept:
        raise SystemExit("no seeds survived MIN_SEED_PIXELS -- wrong sheet?")

    centres = [ndi.center_of_mass(seeds == i) for i in kept]
    rows = cluster([c[0] for c in centres], CELL_GAP)

    # Columns are clustered WITHIN a row: clustering every x-centroid at once
    # fuses several rows' worth of columns and hides a missing cell.
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

    cell_label = {cell: i + 1 for i, cell in enumerate(sorted(set(cell_of_seed.values())))}
    merged = np.zeros_like(seeds)
    for seed_id, cell in cell_of_seed.items():
        merged[seeds == seed_id] = cell_label[cell]

    _, (iy, ix) = ndi.distance_transform_edt(merged == 0, return_indices=True)
    labels = np.where(mask, merged[iy, ix], 0)
    return labels, cell_label


def bleed_rgb(rgba):
    """Flood transparent pixels' RGB with the nearest opaque colour.

    A transparent pixel still carries an RGB triple, and every filter that
    averages neighbours -- mips, the atlas downscale, the bilinear sample that
    draws a 288 px icon into a 48 px inventory slot -- mixes it back in. Leaving
    the sheet's background in fringes each item with it; zeroing it instead rings
    the item with a dark halo. Same fix as the props and minerals cutters.
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
        raise SystemExit(
            f"{spec['name']}: table/sheet disagree -- missing {missing}, extra {extra}")

    records, tiles = [], []
    for row, col, key, display, tag in spec["table"]:
        region = labels == cell_label[(row, col)]
        keep = region & (pixels[:, :, 3] >= TRIM_ALPHA)
        ys, xs = np.where(keep)
        y0, y1 = int(ys.min()), int(ys.max()) + 1
        x0, x1 = int(xs.min()), int(xs.max()) + 1

        if (x1 - x0) > canvas_px or (y1 - y0) > canvas_px:
            raise SystemExit(f"{key}: {x1 - x0}x{y1 - y0} exceeds canvas {canvas_px}")

        # Mask the neighbours out of this item's box before cropping, or a wide
        # item carries a slice of whatever sits beside it.
        cropped = pixels[y0:y1, x0:x1].copy()
        cropped[:, :, 3] *= region[y0:y1, x0:x1]
        cell = Image.fromarray(bleed_rgb(cropped), "RGBA")

        canvas = Image.new("RGBA", (canvas_px, canvas_px), (0, 0, 0, 0))
        canvas.alpha_composite(cell, ((canvas_px - cell.width) // 2,
                                      (canvas_px - cell.height) // 2))

        out = spec["out"] / f"{key}.png"
        rec = {
            "itemKey": key,
            "displayName": display,
            "path": str(out.relative_to(REPO)).replace("\\", "/"),
            "row": row,
            "col": col,
            "sourceBox": [x0, y0, x1, y1],
            "trimmed": [cell.width, cell.height],
            "canvas": canvas_px,
        }
        if tag:
            rec["cuisine"] = tag
        records.append(rec)
        tiles.append(canvas)
        print(f"  {key:<22} {cell.width:>3}x{cell.height:<3} -> {out.name}")

        if not dry_run:
            out.parent.mkdir(parents=True, exist_ok=True)
            canvas.save(out)

    if contact_sheet:
        cols = spec["grid_cols"]
        rows = (len(tiles) + cols - 1) // cols
        img = Image.new("RGBA", (cols * canvas_px, rows * canvas_px), (18, 18, 24, 255))
        for i, tile in enumerate(tiles):
            img.alpha_composite(tile, ((i % cols) * canvas_px, (i // cols) * canvas_px))
        GENERATED.mkdir(parents=True, exist_ok=True)
        path = GENERATED / f"cook_{spec['name']}_contact.png"
        img.save(path)
        print(f"  contact sheet -> {path.relative_to(REPO)}")

    if not dry_run:
        GENERATED.mkdir(parents=True, exist_ok=True)
        manifest = GENERATED / f"cook_items_manifest_{spec['name']}.json"
        manifest.write_text(json.dumps({
            "source": str(spec["sheet"].relative_to(REPO)).replace("\\", "/"),
            "role": spec["name"],
            "canvas": canvas_px,
            "maskAlpha": MASK_ALPHA,
            "seedAlpha": SEED_ALPHA,
            "trimAlpha": TRIM_ALPHA,
            "items": records,
        }, indent=2) + "\n", encoding="utf-8")
        print(f"  manifest -> {manifest.relative_to(REPO)}")

    return len(records)


def main():
    ap = argparse.ArgumentParser(description="Cut the cooking sheets.")
    ap.add_argument("--dry-run", action="store_true", help="segment and report, write nothing")
    ap.add_argument("--contact-sheet", action="store_true", help="also write a contact sheet")
    ap.add_argument("--only", metavar="NAME", help="cut just this sheet (ingredients|dishes)")
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
    print(f"{total} items {'planned' if args.dry_run else 'written'}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
