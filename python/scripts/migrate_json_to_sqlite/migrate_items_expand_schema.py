from __future__ import annotations

import argparse
import json
import shutil
import sqlite3
from pathlib import Path
from typing import Any, Dict, List, Tuple

DB_PATH = Path("data/roguelike.sqlite3")
BACKUP_PATH = Path("data/roguelike.sqlite3.bak")

COLUMNS: List[Tuple[str, str]] = [
    ("effect", "TEXT"),
    ("durability", "INTEGER"),
    ("damage", "INTEGER"),
    ("attack_speed", "REAL"),
    ("range", "INTEGER"),
    ("crit_chance", "REAL"),
    ("crit_multiplier", "REAL"),
    ("weight", "REAL"),
    ("value", "INTEGER"),
    ("quest_id", "TEXT"),
    ("threshold", "INTEGER"),
    ("experience", "INTEGER"),
    ("scale_editor", "REAL"),
    ("scale_map", "REAL"),
    ("scale_inventory", "REAL"),
]


def _get_existing_cols(cur: sqlite3.Cursor, table: str) -> Dict[str, int]:
    rows = cur.execute(f"PRAGMA table_info({table});").fetchall()
    return {str(r[1]).lower(): int(r[0]) for r in rows}


def _safe_get(d: Dict[str, Any], key: str) -> Any:
    try:
        return d.get(key)
    except Exception:
        return None


def _coerce(val: Any, typ: str) -> Any:
    if val is None:
        return None
    try:
        if typ == "INTEGER":
            return int(val)
        if typ == "REAL":
            return float(val)
        if typ == "TEXT":
            return str(val)
    except Exception:
        return None
    return val


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    if not DB_PATH.exists():
        print(f"DB not found: {DB_PATH}")
        return

    con = sqlite3.connect(DB_PATH)
    con.row_factory = sqlite3.Row
    cur = con.cursor()

    # Ensure columns exist
    cols = _get_existing_cols(cur, "items")
    to_add = [(name, typ) for name, typ in COLUMNS if name.lower() not in cols]
    if to_add:
        if not args.dry_run:
            # Backup before schema change
            shutil.copy2(DB_PATH, BACKUP_PATH)
        for name, typ in to_add:
            sql = f"ALTER TABLE items ADD COLUMN {name} {typ};"
            print(f"ADD COLUMN: {sql}")
            if not args.dry_run:
                cur.execute(sql)
        if not args.dry_run:
            con.commit()

    # Backfill from extra_json
    rows = cur.execute("SELECT id, extra_json FROM items").fetchall()
    updates = 0

    for r in rows:
        iid = r["id"]
        js = r["extra_json"]
        if not js:
            continue
        try:
            payload = json.loads(js)
            if not isinstance(payload, dict):
                continue
        except Exception:
            continue
        fields: Dict[str, Any] = {}
        for name, typ in COLUMNS:
            val = _coerce(_safe_get(payload, name), typ)
            if val is not None:
                fields[name] = val
        if not fields:
            continue
        sets = ",".join([f"{k}=?" for k in fields.keys()])
        params = list(fields.values()) + [iid]
        if args.dry_run:
            print(f"DRYRUN UPDATE items SET {sets} WHERE id='{iid}' :: {fields}")
            updates += 1
        else:
            cur.execute(f"UPDATE items SET {sets} WHERE id=?", params)
            updates += 1

    if not args.dry_run:
        con.commit()
    con.close()

    print(f"Backfilled rows: {updates}")


if __name__ == "__main__":
    main()
