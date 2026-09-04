#!/usr/bin/env python3
"""Cut the 8x8 mineral icon sheet into one named, trimmed PNG per mineral.

WHY A TABLE RATHER THAN A CLASSIFIER
------------------------------------
``wave2/classify.py`` sets the precedent and the reason is the same here: what a
lump of painted rock IS cannot be read off its pixels. Hue separates a ruby from
an emerald and says nothing about whether the grey one is iron, silver or plain
stone, and nothing at all about how rare it should be. So the sixty-four rows
below are declared by hand from the rendered sheet, and the tool only does what a
tool can do — segment, trim, place and name.

The rarity column is the other half. It is not decoration: ``LootTable`` derives
a drop weight from ``ItemDefinition.rarity`` when no explicit weight is authored
(Common 600, Uncommon 250, Rare 100, Epic 40, Legendary 10 per mille), so this
column is what makes a seam pour out copper and hand over a starstone once in a
hundred swings. Adding a mineral later means adding a row here and nothing else.

SIZING
------
Icons are written at NATIVE resolution, trimmed to their own alpha and centred on
a square canvas. No upscaling: the sheet's cells are ~134-148px and the shipped
item art is 1024px, so matching the existing files would mean interpolating a
seven-fold blow-up of every one of sixty-four icons and paying for it in the
sprite atlas — which has already had to be lifted to 4096 once today. An
inventory slot draws these at a fraction of even 160px.

USAGE
-----
    python tools/atlas/minerals/build_mineral_icons.py \
        staging/items/minerals/mineral_and_crystal_icons_8x8.png
"""

from __future__ import annotations

import argparse
import json
import os
from pathlib import Path

import numpy as np
from PIL import Image

REPO = Path(__file__).resolve().parents[3]
ART_OUT = REPO / "unity/Valkur/Assets/_Project/Art/Items/minerals"
MANIFEST = REPO / "tools/atlas/generated/mineral_icons_manifest.json"

#: Square canvas each trimmed icon is centred on. Comfortably above the widest
#: cell so nothing is ever scaled, and a power-of-two-friendly size for packing.
ICON_CANVAS = 160

#: Alpha at or above which a pixel counts as part of an icon.
ALPHA_FLOOR = 8

#: Occupied pixels a band needs before it counts as a column or row of the grid.
#: The shipped sheet carries a single stray pixel at x=773 that opens a NINTH
#: column band of exactly 1px and 1 opaque pixel, against ~95,000 for every real
#: column. Without this the grid check refuses the sheet, and a tool that made the
#: operator hand-edit the art to satisfy it would be the wrong answer -- the speck
#: is in the source and the source is what we were given. slice_prop_sheet carries
#: its own speck filter for the same reason.
MIN_BAND_PIXELS = 500

#: Transparent pixels carry zeroed RGB, so resampling or trimming in straight
#: alpha bleeds black into the edges and rings every icon with a dark halo. The
#: sheet is composited in PREMULTIPLIED space for the same reason
#: build_building_props.py does it.
PREMULTIPLIED = "RGBa"

# (row, col) -> (item id, Spanish display name, rarity, one-line description)
#
# Rarity drives the drop weight, so the ladder is the balance: the eight rows run
# roughly from what a seam is MADE of to what it hides once in a career.
MINERALS = [
    # ── row 0 — the plain rock a seam is made of ────────────────────────────
    ("stone_chunk",      "Trozo de Piedra",      "Common",    "Roca comun sin valor mas alla de la construccion."),
    ("tin_ore",          "Mineral de Estano",    "Common",    "Nodulos pardos de estano, base de toda aleacion barata."),
    ("iron_ore",         "Mineral de Hierro",    "Common",    "Vetas de oxido atravesando la roca gris."),
    ("copper_ore",       "Mineral de Cobre",     "Common",    "Cristales cobrizos de brillo calido."),
    ("coal_chunk",       "Carbon",               "Common",    "Carbon mineral. Arde largo y sucio."),
    ("silver_ore",       "Mineral de Plata",     "Uncommon",  "Nodulos plateados de lustre frio."),
    ("quartz_ore",       "Cuarzo Bruto",         "Common",    "Agujas de cuarzo claro sobre roca."),
    ("gold_ore",         "Mineral de Oro",       "Uncommon",  "Oro nativo en masa, sin refinar."),

    # ── row 1 — better grades of the same ──────────────────────────────────
    ("limestone_chunk",  "Caliza",               "Common",    "Piedra clara y blanda, facil de trabajar."),
    ("hematite_ore",     "Hematita",             "Common",    "Hierro rojo, denso y pesado en la mano."),
    ("limonite_ore",     "Limonita",             "Common",    "Hierro pardo de vetas herrumbrosas."),
    ("bronze_ore",       "Mineral de Bronce",    "Uncommon",  "Cristales dorados de aleacion natural."),
    ("pyrite_ore",       "Pirita",               "Common",    "El oro de los tontos, brillante y quebradizo."),
    ("cobalt_ore",       "Mineral de Cobalto",   "Uncommon",  "Azul metalico. Endurece cualquier filo."),
    ("moonstone_ore",    "Piedra Lunar Bruta",   "Rare",      "Cristales lechosos que atrapan la luz."),
    ("gold_rich_ore",    "Veta Aurifera",        "Rare",      "Oro macizo. Una veta asi financia un pueblo."),

    # ── row 2 — the deep, hot and volcanic ─────────────────────────────────
    ("obsidian_chunk",   "Obsidiana",            "Uncommon",  "Vidrio volcanico de filo imposible."),
    ("anthracite_chunk", "Antracita",            "Uncommon",  "Carbon negro y compacto. Arde limpio."),
    ("ember_ore",        "Roca de Brasa",        "Rare",      "Roca cuarteada con rescoldos aun vivos."),
    ("magma_ore",        "Nucleo de Magma",      "Epic",      "Piedra fundida que no termina de enfriarse."),
    ("cinnabar_ore",     "Cinabrio",             "Uncommon",  "Rojo intenso, hermoso y venenoso."),
    ("tanzanite_ore",    "Tanzanita Bruta",      "Rare",      "Cristal azul violaceo de las simas."),
    ("scoria_chunk",     "Escoria Volcanica",    "Common",    "Roca porosa y ligera, escupida por la tierra."),
    ("gold_nugget",      "Pepita de Oro",        "Rare",      "Una sola pepita, pura y pesada."),

    # ── row 3 — the first real crystals ────────────────────────────────────
    ("malachite_ore",    "Malaquita",            "Uncommon",  "Verde en bandas sobre matriz de cobre."),
    ("emerald_raw",      "Esmeralda Bruta",      "Rare",      "Prismas verdes creciendo hacia la luz."),
    ("turquoise_ore",    "Turquesa",             "Uncommon",  "Azul verdoso moteado, calido al tacto."),
    ("aquamarine_raw",   "Aguamarina Bruta",     "Rare",      "Agujas del color del mar poco profundo."),
    ("azurite_ore",      "Azurita",              "Uncommon",  "Azul profundo incrustado en roca."),
    ("sapphire_raw",     "Zafiro Bruto",         "Rare",      "Cristales azules con luz propia."),
    ("lapis_ore",        "Lapislazuli",          "Uncommon",  "Azul nocturno salpicado de oro."),
    ("amethyst_raw",     "Amatista Bruta",       "Rare",      "Puntas violetas sobre costra de cuarzo."),

    # ── row 4 — cut-grade crystal clusters ─────────────────────────────────
    ("clear_quartz",     "Cuarzo Cristalino",    "Uncommon",  "Puntas transparentes, sin una sola nube."),
    ("smoky_quartz",     "Cuarzo Ahumado",       "Uncommon",  "Cuarzo pardo, como humo congelado."),
    ("amethyst_cluster", "Racimo de Amatista",   "Rare",      "Un racimo entero, digno de un cetro."),
    ("kunzite_raw",      "Kunzita",              "Rare",      "Rosa intenso que palidece al sol."),
    ("ruby_raw",         "Rubi Bruto",           "Epic",      "Rojo sangre encerrado en su matriz."),
    ("garnet_cluster",   "Racimo de Granate",    "Rare",      "Puntas rojo fuego apinadas."),
    ("citrine_geode",    "Geoda de Citrino",     "Rare",      "Una geoda partida, naranja por dentro."),
    ("topaz_cluster",    "Racimo de Topacio",    "Rare",      "Ambar dorado en agujas limpias."),

    # ── row 5 — gems still in the rock ─────────────────────────────────────
    ("emerald_cluster",  "Racimo de Esmeralda",  "Epic",      "Verde profundo, sin una fractura."),
    ("emerald_in_matrix","Esmeralda en Matriz",  "Rare",      "Una sola gema verde asomando de la roca."),
    ("sapphire_cabochon","Zafiro en Matriz",     "Epic",      "Azul pulido por la propia montana."),
    ("sapphire_cluster", "Racimo de Zafiro",     "Epic",      "Varias gemas azules en un mismo nucleo."),
    ("amethyst_geode",   "Geoda de Amatista",    "Rare",      "Violeta oscuro dentro de piedra gris."),
    ("pink_sapphire",    "Zafiro Rosa",          "Epic",      "Rarisimo entre los zafiros."),
    ("ruby_in_matrix",   "Rubi en Matriz",       "Epic",      "Rojo puro sujeto todavia por la roca."),
    ("amber_in_matrix",  "Ambar en Matriz",      "Rare",      "Resina antigua endurecida en la veta."),

    # ── row 6 — the strange ones ───────────────────────────────────────────
    ("opal_raw",         "Opalo de Fuego",       "Epic",      "Cambia de color segun donde lo mires."),
    ("milk_opal",        "Opalo Lechoso",        "Rare",      "Blanco calido con destellos escondidos."),
    ("moonstone_gem",    "Piedra Lunar",         "Epic",      "Una luz azul se desplaza bajo su superficie."),
    ("starstone_ore",    "Piedra Estelar",       "Legendary", "Guarda un cielo nocturno entero dentro."),
    ("verdant_opal",     "Opalo Verde",          "Epic",      "Verde azulado con fuego interior."),
    ("void_orb_ore",     "Orbe del Vacio",       "Legendary", "Una esfera que se traga la luz que recibe."),
    ("magma_heart",      "Corazon de Magma",     "Legendary", "Late. La roca alrededor nunca se enfria."),
    ("void_crystal",     "Cristal del Vacio",    "Legendary", "Cristales que brillan sin fuente alguna."),

    # ── row 7 — the once-in-a-career finds ─────────────────────────────────
    ("verdant_crystal",  "Cristal Verdeante",    "Epic",      "Crece sobre madera viva y sigue creciendo."),
    ("frost_crystal",    "Cristal de Escarcha",  "Rare",      "Frio al tacto incluso junto al fuego."),
    ("glacier_crystal",  "Cristal de Glaciar",   "Epic",      "Hielo que lleva mil anos sin derretirse."),
    ("sunstone_cluster", "Racimo de Sol",        "Epic",      "Guarda calor mucho despues del anochecer."),
    ("toxic_ore",        "Veta Toxica",          "Rare",      "Vetas verdes que ninguna mano deberia tocar."),
    ("shadow_crystal",   "Cristal de Sombra",    "Epic",      "Purpura tan oscuro que parece un agujero."),
    ("celestial_geode",  "Geoda Celestial",      "Legendary", "Cuarzo blanco atado con oro que nadie fundio."),
    ("eternal_crystal",  "Cristal Eterno",       "Legendary", "Hielo y oro en la misma piedra. No se agrieta."),
]

ROWS = COLS = 8


def bands(mask_1d: np.ndarray, weight: np.ndarray) -> list[tuple[int, int]]:
    """Inclusive (start, end) spans of the occupied runs along one axis.

    ``weight`` is the per-index count of opaque pixels, used to drop specks: a
    band carrying a handful of pixels is an artefact of the source art, not a
    column of the grid.
    """
    out: list[tuple[int, int]] = []
    start = None
    for i, filled in enumerate(mask_1d):
        if filled and start is None:
            start = i
        elif not filled and start is not None:
            out.append((start, i - 1))
            start = None
    if start is not None:
        out.append((start, len(mask_1d) - 1))

    kept, dropped = [], []
    for s, e in out:
        (kept if weight[s:e + 1].sum() >= MIN_BAND_PIXELS else dropped).append((s, e))
    for s, e in dropped:
        print(f"  speck ignored at {s}-{e} ({int(weight[s:e + 1].sum())}px)")
    return kept


def cut(sheet_path: Path, dry_run: bool) -> int:
    sheet = Image.open(sheet_path).convert("RGBA")
    alpha = np.array(sheet)[:, :, 3] >= ALPHA_FLOOR

    col_bands = bands(alpha.any(axis=0), alpha.sum(axis=0))
    row_bands = bands(alpha.any(axis=1), alpha.sum(axis=1))

    if len(col_bands) != COLS or len(row_bands) != ROWS:
        raise SystemExit(
            f"expected a {ROWS}x{COLS} grid, found {len(row_bands)} rows and "
            f"{len(col_bands)} columns -- refusing to guess")

    if len(MINERALS) != ROWS * COLS:
        raise SystemExit(f"table has {len(MINERALS)} rows, grid holds {ROWS * COLS}")

    ART_OUT.mkdir(parents=True, exist_ok=True)
    records = []

    for r, (y0, y1) in enumerate(row_bands):
        for c, (x0, x1) in enumerate(col_bands):
            item_id, display, rarity, blurb = MINERALS[r * COLS + c]

            cell = sheet.crop((x0, y0, x1 + 1, y1 + 1))

            # Trim to the icon's OWN alpha, then centre. Cutting on the grid alone
            # leaves each icon off-centre by however far it was drawn from its cell
            # centre, which reads as the inventory grid being misaligned.
            box = cell.convert(PREMULTIPLIED).getbbox()
            if box:
                cell = cell.crop(box)

            if cell.width > ICON_CANVAS or cell.height > ICON_CANVAS:
                raise SystemExit(
                    f"{item_id} is {cell.width}x{cell.height}, larger than the "
                    f"{ICON_CANVAS}px canvas -- raise ICON_CANVAS rather than scaling")

            canvas = Image.new("RGBA", (ICON_CANVAS, ICON_CANVAS), (0, 0, 0, 0))
            canvas.alpha_composite(cell, ((ICON_CANVAS - cell.width) // 2,
                                          (ICON_CANVAS - cell.height) // 2))

            out = ART_OUT / f"{item_id}.png"
            if not dry_run:
                canvas.save(out)

            records.append({
                "itemId": item_id,
                "displayName": display,
                "rarity": rarity,
                "description": blurb,
                "row": r, "col": c,
                "sprite": f"Assets/_Project/Art/Items/minerals/{item_id}.png",
                "trimmed": [cell.width, cell.height],
            })

    if not dry_run:
        MANIFEST.parent.mkdir(parents=True, exist_ok=True)
        with open(MANIFEST, "w", encoding="utf-8") as fh:
            json.dump({"source": os.path.relpath(sheet_path, REPO).replace("\\", "/"),
                       "canvas": ICON_CANVAS,
                       "count": len(records),
                       "minerals": records}, fh, indent=2, ensure_ascii=False)

    by_rarity: dict[str, int] = {}
    for rec in records:
        by_rarity[rec["rarity"]] = by_rarity.get(rec["rarity"], 0) + 1
    print(f"{len(records)} icons at {ICON_CANVAS}x{ICON_CANVAS} -> {ART_OUT}")
    for tier in ("Common", "Uncommon", "Rare", "Epic", "Legendary"):
        print(f"  {tier:<10} {by_rarity.get(tier, 0)}")
    if not dry_run:
        print(f"manifest -> {MANIFEST}")
    return 0


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("sheet", type=Path)
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()
    return cut(args.sheet, args.dry_run)


if __name__ == "__main__":
    raise SystemExit(main())
