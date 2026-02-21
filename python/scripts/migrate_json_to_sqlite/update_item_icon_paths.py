from __future__ import annotations

import argparse
import json
import shutil
import sqlite3
from pathlib import Path
from typing import Dict, Tuple

DB_PATH = Path("data/roguelike.sqlite3")
BACKUP_PATH = Path("data/roguelike.sqlite3.bak")

# Mapping: basename -> subpath under assets/items/
BASENAME_MAP: Dict[str, str] = {
    # Experience orbs
    "exp_orb_1.png": "experience/exp_orb_1.png",
    "exp_orb_2.png": "experience/exp_orb_2.png",
    "exp_orb_3.png": "experience/exp_orb_3.png",
    "exp_orb_4.png": "experience/exp_orb_4.png",
    # Coins
    "gold_coin_stack_1.png": "bank/gold_coin_stack_1.png",
    "gold_coin_stack_2.png": "bank/gold_coin_stack_2.png",
    # Lumberjack
    "wood_log_bundle.png": "lumberjack/wood_log_bundle.png",
    # Potions
    "health_potion.png": "Alchemy/health_potion.png",
    "mana_potion.png": "Alchemy/mana_potion.png",
    "energy_potion.png": "Alchemy/energy_potion.png",
    "poison_potion.png": "Alchemy/poison_potion.png",
    "explosion_potion.png": "Alchemy/explosion_potion.png",
    # Magic
    "wizard_staff_lvl_1.png": "magic/wizard_staff_lvl_1.png",
    "wizard_staff_lvl_2.png": "magic/wizard_staff_lvl_2.png",
    "wizard_staff_lvl_3.png": "magic/wizard_staff_lvl_3.png",
    # Mining
    "iron_ingot.png": "Mining/iron_ingot.png",
    # Cook
    "food_chicken.png": "Cook/food_chicken.png",
    # NPCs
    "ancient_relic_mask.png": "npcs/ancient_relic_mask.png",
}

PREFIXES = (
    "assets/items/",
    "items/",
)


def normalize_path(p: str) -> str:
    """Unify separators and drop any leading './'"""
    s = p.replace("\\", "/").lstrip("./")
    return s


def rewrite_item_path(p: str) -> Tuple[str, bool]:
    s = normalize_path(p)
    base = s.split("/")[-1]
    new_rel = BASENAME_MAP.get(base)
    if not new_rel:
        return p, False
    new_path = f"assets/items/{new_rel}"
    if s == new_path:
        return p, False
    return new_path, True


def rewrite_icon_json(js: str) -> Tuple[str, int]:
    try:
        arr = json.loads(js)
        if not isinstance(arr, list):
            return js, 0
    except Exception:
        return js, 0
    changed = 0
    out = []
    for v in arr:
        if isinstance(v, str):
            new_v, did = rewrite_item_path(v)
            if did:
                changed += 1
            out.append(new_v)
        else:
            out.append(v)
    if changed:
        return json.dumps(out, ensure_ascii=False, separators=(",", ":")), changed
    return js, 0


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--dry-run", action="store_true", help="Do not write changes to the database")
    args = ap.parse_args()

    if not DB_PATH.exists():
        print(f"DB not found: {DB_PATH}")
        return

    total_updates = 0
    by_field = {"icon_small": 0, "icon_large": 0, "icon_json": 0}

    con = sqlite3.connect(DB_PATH)
    con.row_factory = sqlite3.Row
    cur = con.cursor()

    rows = cur.execute("SELECT id, icon_small, icon_large, icon_json FROM items").fetchall()

    updates = []  # (id, field, new_value)
    for r in rows:
        iid = r["id"]
        small = r["icon_small"]
        large = r["icon_large"]
        j = r["icon_json"]

        if isinstance(small, str) and small:
            new, did = rewrite_item_path(small)
            if did:
                updates.append((iid, "icon_small", new))
                by_field["icon_small"] += 1
        if isinstance(large, str) and large:
            new, did = rewrite_item_path(large)
            if did:
                updates.append((iid, "icon_large", new))
                by_field["icon_large"] += 1
        if isinstance(j, str) and j:
            new, changed = rewrite_icon_json(j)
            if changed:
                updates.append((iid, "icon_json", new))
                by_field["icon_json"] += changed

    print(f"Scanned items: {len(rows)}")
    print(f"Planned updates: {len(updates)} (by field: {by_field})")

    if not updates:
        con.close()
        return

    if args.dry_run:
        for iid, field, new_val in updates[:25]:
            print(f"DRYRUN sample: id={iid} field={field} -> {new_val}")
        con.close()
        return

    # Backup before writing
    if DB_PATH.exists():
        shutil.copy2(DB_PATH, BACKUP_PATH)
        print(f"Backup created: {BACKUP_PATH}")

    for iid, field, new_val in updates:
        cur.execute(f"UPDATE items SET {field}=? WHERE id=?", (new_val, iid))
        total_updates += 1
    con.commit()
    con.close()

    print(f"Applied updates: {total_updates}")


if __name__ == "__main__":
    main()
