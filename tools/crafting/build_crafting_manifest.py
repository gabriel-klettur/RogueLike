#!/usr/bin/env python3
"""Resolve every trade's items and recipes into ONE manifest the Unity importer reads.

WHY A GENERATOR AND NOT HAND-TYPED ROWS
---------------------------------------
Every number a crafted item carries -- what it heals, what it feeds, what it is worth,
how rare it is, how much experience it teaches -- is a FUNCTION of the recipe that makes
it. Typing those rows by hand produces a table nobody can re-balance: an item built from
eight ingredients and one built from three would drift apart on taste rather than on
cost, and the first retune of beef would leave every beef dish priced against the old
value with nothing to say so.

So the ingredients carry the authored numbers -- one line each -- and the products
DERIVE. Changing what beef is worth re-prices the asado, the arepa and the milanesa in
one edit, and the derivation is written down here rather than living in a designer's
head.

The one thing NOT derived is what an item is called and what it is: a display name and a
description are prose, and no formula writes them. Those come from the slicer's tables,
which is also the file that already had to name every cell to cut it.

PROFESSIONS
-----------
Five trades ship: cooking, blacksmith, mining, lumberjack, and a generic crafting bucket
for anything that belongs to none of the others. Only COOKING has recipes today -- the
other four are declared so the tab, the level curve and the station vocabulary exist the
moment their recipes are written, and so that adding a recipe to blacksmithing is a data
edit rather than a code change.

A trade with no recipes draws an empty tab that says so. That is deliberate and is the
honest state: hiding it would make "blacksmithing exists but has nothing yet"
indistinguishable from "blacksmithing was never added".

WHAT IT WRITES
--------------
``tools/atlas/generated/crafting_manifest.json`` -- professions, items (id, names, art
path, resolved stats) and recipes (profession, output, ingredient list, station and level
requirements, xp). ``CraftingContentImporter`` reads exactly that and creates the assets.
The importer does no arithmetic, which keeps the balance auditable from outside Unity.

USAGE
-----
    python tools/crafting/build_crafting_manifest.py --dry-run
    python tools/crafting/build_crafting_manifest.py
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
GENERATED = REPO / "tools/atlas/generated"
RECIPES_DIR = REPO / "tools/crafting/recipes"

#: The trades. Adding one here plus a recipe file under ``recipes/`` is the whole cost of
#: a new profession -- no Unity code changes, because ProfessionDefinition is an asset and
#: the panel builds its tab strip from the catalog.
#:
#: key -> (display name, accent RGB, sort order, station noun, max level, base xp, growth)
PROFESSIONS = {
    "cooking":    ("Cocina",     (0.93, 0.62, 0.28), 0, "una cocina",       20, 100, 1.25),
    "blacksmith": ("Herreria",   (0.72, 0.45, 0.30), 1, "una fragua",       20, 120, 1.28),
    "mining":     ("Mineria",    (0.55, 0.60, 0.70), 2, "una mina",         20, 110, 1.26),
    "lumberjack": ("Talado",     (0.45, 0.66, 0.38), 3, "un aserradero",    20, 110, 1.26),
    "crafting":   ("Artesania",  (0.62, 0.55, 0.85), 4, "un banco de trabajo", 20, 100, 1.25),
}

#: itemKey -> (gold value, weight, hunger it restores raw, tier label)
#:
#: The ONE authored table for cooking. Prices are a rough market: an animal protein costs
#: three to four times a root vegetable, a dairy or a spice sits between them, and the
#: three ground staples (flour, cornmeal, rice) are cheap because every cuisine leans on
#: them and a staple nobody can afford stops six recipes at once.
#:
#: `raw hunger` is deliberately small and non-zero. It is what makes an ingredient an
#: emergency ration rather than an inert token -- chewing a raw potato should be possible
#: and clearly worse than cooking it. It is ALSO what would file the item wrongly: a
#: non-zero hunger sends `ItemCategoryUtil.GetCategory` to Consumable, which is not where
#: an ingredient belongs, so the importer keeps it OFF the item. See the
#: RAW_INGREDIENTS_ARE_MATERIALS note there.
INGREDIENTS = {
    # proteins
    "beef":        (12, 0.60, 8, "protein"),
    "chicken":     (9,  0.45, 7, "protein"),
    "fish":        (9,  0.40, 7, "protein"),
    "sausage":     (10, 0.35, 7, "protein"),
    "egg":         (4,  0.10, 4, "protein"),
    # aromatics and vegetables
    "potato":      (3,  0.25, 4, "vegetable"),
    "onion":       (3,  0.15, 2, "vegetable"),
    "garlic":      (3,  0.05, 1, "aromatic"),
    "tomato":      (4,  0.15, 3, "vegetable"),
    "bell_pepper": (5,  0.15, 3, "vegetable"),
    "carrot":      (3,  0.15, 3, "vegetable"),
    "beet":        (3,  0.20, 3, "vegetable"),
    "cabbage":     (3,  0.30, 3, "vegetable"),
    "corn":        (4,  0.20, 4, "vegetable"),
    "avocado":     (6,  0.20, 5, "vegetable"),
    # dairy and larder
    "cheese":      (8,  0.25, 6, "dairy"),
    "butter":      (6,  0.15, 4, "dairy"),
    "sour_cream":  (5,  0.20, 3, "dairy"),
    "rice":        (4,  0.30, 5, "staple"),
    "black_beans": (4,  0.30, 5, "staple"),
    # dry goods
    "flour":       (3,  0.35, 3, "staple"),
    "cornmeal":    (3,  0.35, 3, "staple"),
    "paprika":     (5,  0.05, 0, "aromatic"),
    "herbs":       (4,  0.05, 0, "aromatic"),
    "plantain":    (4,  0.25, 5, "vegetable"),
}

#: Crafting multiplies what the ingredients were worth. 1.75 is chosen so the cheapest
#: product still beats selling its parts (hakarl: 3 fish at 9 = 27 raw, 47 cooked) -- if
#: crafting ever lost money the whole system would be a trap.
CRAFT_VALUE_MULTIPLIER = 1.75

#: A vendor buys back at this fraction of value. Matches the ratio the shipped food items
#: already used (value 15, sell 7).
SELL_FRACTION = 0.45

#: Cooked food restores this much hunger per unit of raw hunger it was made from, so
#: cooking is a straight 1.6x on nutrition as well as on price.
COOK_HUNGER_GAIN = 1.6

#: Healing comes from PROTEIN and DAIRY only. A gazpacho is three tomatoes and should feed
#: you without being a health potion; an asado is three cuts of beef and should mend
#: something. Splitting the two stops "big recipe" and "healing recipe" being the same
#: axis, which is what would make every long recipe strictly better than every short one.
#:
#: The floor is not decoration. On protein and dairy alone, gazpacho resolves to healing 0,
#: and a cooked dish that mends nothing reads as an item that failed to work rather than as
#: a light one. Every dish mends at least this much.
HEAL_PER_PROTEIN_UNIT = 4.0
HEAL_PER_DAIRY_UNIT = 2.0
HEAL_FLOOR = 2

#: An ingredient count at or above this makes a recipe a STATION recipe: it cannot be made
#: from the bare inventory panel and needs a station for its trade in range. Set at 6
#: distinct ingredients -- the complex ones, which over the 36 cooking recipes is 8:
#: borscht, cazuela_chilena, hallaca, holubtsi, kjotsupa, locro, paella and varenyky.
STATION_INGREDIENT_THRESHOLD = 6

#: Experience per craft, derived from the recipe's raw cost so a hallaca teaches more than
#: a skyr. Scaled down from gold because the two are not the same currency and a 1:1 ratio
#: would level a trade in a handful of crafts.
XP_PER_RAW_VALUE = 0.55
XP_FLOOR = 4

#: Rarity ladder, keyed on the product's resolved gold value. Rarity is a READOUT of cost
#: here, not a second dial: two items of the same worth reading as different rarities is
#: exactly the drift the derivation exists to prevent.
#: (threshold, ItemRarity ordinal) -- Common 0, Uncommon 1, Rare 2, Epic 3.
#:
#: The thresholds are set against the range the recipes ACTUALLY produce, which for cooking
#: is 28..93. A ladder whose top rungs sit above the highest reachable value is not a
#: strict ladder, it is a dead one -- the first pass used the round numbers 60/100/150 and
#: shipped 20 Common, 16 Uncommon and no dish that could ever be Rare. ``--dry-run`` prints
#: the spread for exactly this check; re-read it after any change to a price, a recipe or
#: CRAFT_VALUE_MULTIPLIER.
RARITY_LADDER = [(0, 0), (48, 1), (66, 2), (84, 3)]

#: How long one craft takes, in seconds. Scales with the recipe so a hakarl is quick and a
#: hallaca is not, and every product is fast enough that the panel is not a waiting room.
CRAFT_SECONDS_BASE = 0.8
CRAFT_SECONDS_PER_UNIT = 0.12

CUISINE_ES = {
    "ukraine": "Ucrania",
    "chile": "Chile",
    "spain": "Espana",
    "iceland": "Islandia",
    "argentina": "Argentina",
    "venezuela": "Venezuela",
}


def load_slicer_tables():
    """Pull the authored names straight out of the slicer.

    Importing rather than duplicating: the slicer's tables are what named the PNG files,
    so a second copy here would be a rename waiting to disagree with the art it points at.
    """
    import importlib.util

    spec = importlib.util.spec_from_file_location(
        "cook_slicer", REPO / "tools/atlas/wave10/build_cook_items.py")
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod.INGREDIENTS, mod.DISHES


def rarity_for(value):
    out = 0
    for threshold, ordinal in RARITY_LADDER:
        if value >= threshold:
            out = ordinal
    return out


def build_professions():
    records = []
    for key, (name, rgb, order, station, cap, base_xp, growth) in PROFESSIONS.items():
        records.append({
            "professionKey": key,
            "displayName": name,
            "description": f"Oficio de {name.lower()}.",
            "accentColor": {"r": rgb[0], "g": rgb[1], "b": rgb[2], "a": 1.0},
            "sortOrder": order,
            "stationName": station,
            "maxLevel": cap,
            "baseXpPerLevel": base_xp,
            "xpGrowth": growth,
        })
    return records


def build_cooking(items, recipes):
    """Append cooking's 25 ingredients, 36 dishes and 36 recipes to the manifest lists."""
    source = json.loads((RECIPES_DIR / "cooking.json").read_text(encoding="utf-8"))
    ing_table, dish_table = load_slicer_tables()

    ing_names = {key: name for _, _, key, name in ing_table}
    dish_names = {key: (name, cuisine) for _, _, key, name, cuisine in dish_table}

    mismatch = sorted(set(INGREDIENTS) ^ set(ing_names))
    if mismatch:
        raise SystemExit(f"ingredient table vs slicer disagree: {mismatch}")

    used = set()
    for parts in source.values():
        used |= set(parts)
    missing = sorted(used - set(INGREDIENTS))
    if missing:
        raise SystemExit(f"recipes name ingredients with no art or price: {missing}")

    missing_dishes = sorted(set(source) - set(dish_names))
    if missing_dishes:
        raise SystemExit(f"recipes produce dishes with no art: {missing_dishes}")
    uncooked = sorted(set(dish_names) - set(source))
    if uncooked:
        raise SystemExit(f"dishes with no recipe: {uncooked}")

    for key, (value, weight, raw_hunger, tier) in sorted(INGREDIENTS.items()):
        items.append({
            "itemId": key,
            "displayName": ing_names[key],
            "description": f"Ingrediente de cocina ({tier}). Se usa para preparar platos.",
            "role": "ingredient",
            "profession": "cooking",
            "art": f"Assets/_Project/Art/Items/cook/ingredients/{key}.png",
            "value": value,
            "sellPrice": max(1, round(value * SELL_FRACTION)),
            "weight": weight,
            "rarity": 0,
            "stackable": True,
            "maxStack": 50,
            "healing": 0,
            "hunger": 0,
            "rawHunger": raw_hunger,
        })

    for key in sorted(source):
        parts = source[key]
        display, cuisine = dish_names[key]

        raw_value = sum(INGREDIENTS[i][0] * q for i, q in parts.items())
        raw_hunger = sum(INGREDIENTS[i][2] * q for i, q in parts.items())
        protein = sum(q for i, q in parts.items() if INGREDIENTS[i][3] == "protein")
        dairy = sum(q for i, q in parts.items() if INGREDIENTS[i][3] == "dairy")
        units = sum(parts.values())

        value = round(raw_value * CRAFT_VALUE_MULTIPLIER)
        healing = max(HEAL_FLOOR,
                      round(protein * HEAL_PER_PROTEIN_UNIT + dairy * HEAL_PER_DAIRY_UNIT))
        hunger = round(raw_hunger * COOK_HUNGER_GAIN)

        items.append({
            "itemId": key,
            "displayName": display,
            "description": f"Plato tradicional de {CUISINE_ES[cuisine]}. "
                           f"Restaura {healing} de vida y {hunger} de hambre.",
            "role": "product",
            "profession": "cooking",
            "group": cuisine,
            "art": f"Assets/_Project/Art/Items/cook/dishes/{key}.png",
            "value": value,
            "sellPrice": max(1, round(value * SELL_FRACTION)),
            "weight": round(0.25 + units * 0.06, 2),
            "rarity": rarity_for(value),
            "stackable": True,
            "maxStack": 5,
            "healing": healing,
            "hunger": hunger,
            "rawHunger": 0,
        })

        recipes.append({
            "recipeId": key,
            "displayName": display,
            "profession": "cooking",
            "group": cuisine,
            "outputItemId": key,
            "outputQuantity": 1,
            "requiresStation": len(parts) >= STATION_INGREDIENT_THRESHOLD,
            "requiredLevel": 1,
            "xpReward": max(XP_FLOOR, round(raw_value * XP_PER_RAW_VALUE)),
            "craftSeconds": round(CRAFT_SECONDS_BASE + units * CRAFT_SECONDS_PER_UNIT, 2),
            "ingredients": [{"itemId": i, "quantity": parts[i]} for i in sorted(parts)],
        })


def build():
    items, recipes = [], []
    build_cooking(items, recipes)
    return {
        "generator": "tools/crafting/build_crafting_manifest.py",
        "craftValueMultiplier": CRAFT_VALUE_MULTIPLIER,
        "stationIngredientThreshold": STATION_INGREDIENT_THRESHOLD,
        "professions": build_professions(),
        "items": items,
        "recipes": recipes,
    }


def main():
    ap = argparse.ArgumentParser(description="Resolve the crafting manifest.")
    ap.add_argument("--dry-run", action="store_true", help="report, write nothing")
    args = ap.parse_args()

    doc = build()
    products = [i for i in doc["items"] if i["role"] == "product"]
    ings = [i for i in doc["items"] if i["role"] == "ingredient"]
    station = [r for r in doc["recipes"] if r["requiresStation"]]

    print(f"  {len(doc['professions'])} professions: "
          f"{', '.join(p['professionKey'] for p in doc['professions'])}")
    print(f"  {len(ings)} ingredients, {len(products)} products, {len(doc['recipes'])} recipes")

    by_profession = {}
    for r in doc["recipes"]:
        by_profession[r["profession"]] = by_profession.get(r["profession"], 0) + 1
    for p in doc["professions"]:
        count = by_profession.get(p["professionKey"], 0)
        note = "" if count else "   (no recipes yet — draws an empty tab, deliberately)"
        print(f"    {p['professionKey']:<12} {count:>3} recipes{note}")

    print(f"  {len(station)} recipes need a station: "
          f"{', '.join(r['recipeId'] for r in station)}")
    if products:
        print(f"  product value {min(d['value'] for d in products)}..{max(d['value'] for d in products)}, "
              f"healing {min(d['healing'] for d in products)}..{max(d['healing'] for d in products)}, "
              f"xp {min(r['xpReward'] for r in doc['recipes'])}..{max(r['xpReward'] for r in doc['recipes'])}")
        by_rarity = {}
        for d in products:
            by_rarity[d["rarity"]] = by_rarity.get(d["rarity"], 0) + 1
        print(f"  rarity spread {dict(sorted(by_rarity.items()))}")

    if not args.dry_run:
        GENERATED.mkdir(parents=True, exist_ok=True)
        out = GENERATED / "crafting_manifest.json"
        out.write_text(json.dumps(doc, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
        print(f"  manifest -> {out.relative_to(REPO)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
