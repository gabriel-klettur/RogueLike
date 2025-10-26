from __future__ import annotations

"""
Normalize items table defaults:
- Set description to '' where NULL
- Set stackable to 1 where NULL

Usage:
  python -m scripts.database.normalize_items_defaults [--apply] [--db data/roguelike.sqlite3]
"""

import argparse
import sqlite3
from pathlib import Path

DB_DEFAULT = Path("data/roguelike.sqlite3")


def main() -> None:
    ap = argparse.ArgumentParser(description="Normalize items table defaults")
    ap.add_argument("--db", default=str(DB_DEFAULT), help="SQLite database path")
    ap.add_argument("--apply", action="store_true", help="Apply updates (otherwise dry-run)")
    args = ap.parse_args()

    db_path = Path(args.db)
    con = sqlite3.connect(db_path)
    try:
        cur = con.cursor()
        # Counts before
        total = cur.execute("SELECT COUNT(*) FROM items").fetchone()[0]
        null_desc = cur.execute("SELECT COUNT(*) FROM items WHERE description IS NULL").fetchone()[0]
        null_stack = cur.execute("SELECT COUNT(*) FROM items WHERE stackable IS NULL").fetchone()[0]
        print(f"Total rows: {total}")
        print(f"Will set description='' for rows: {null_desc}")
        print(f"Will set stackable=1 for rows: {null_stack}")

        if args.apply:
            cur.execute("UPDATE items SET description='' WHERE description IS NULL")
            cur.execute("UPDATE items SET stackable=1 WHERE stackable IS NULL")
            con.commit()
            # Counts after
            null_desc2 = cur.execute("SELECT COUNT(*) FROM items WHERE description IS NULL").fetchone()[0]
            null_stack2 = cur.execute("SELECT COUNT(*) FROM items WHERE stackable IS NULL").fetchone()[0]
            print("Applied updates.")
            print(f"Remaining NULL description: {null_desc2}")
            print(f"Remaining NULL stackable: {null_stack2}")
        else:
            print("Dry-run. Use --apply to perform updates.")
    finally:
        con.close()


if __name__ == "__main__":
    main()
