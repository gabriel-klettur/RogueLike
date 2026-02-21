from __future__ import annotations

import json
import sqlite3
from pathlib import Path

DB_PATH = Path("data/roguelike.sqlite3")


def main() -> None:
    con = sqlite3.connect(DB_PATH)
    con.row_factory = sqlite3.Row
    try:
        cols = [r[1] for r in con.execute("PRAGMA table_info(items)").fetchall()]
        print("columns:", cols)
        count = con.execute("SELECT COUNT(*) FROM items").fetchone()[0]
        print("row_count:", count)
        rows = con.execute(
            "SELECT id, name, description, stackable, max_stack, icon_small, icon_large, icon_json "
            "FROM items ORDER BY id LIMIT 20"
        ).fetchall()
        print("first_rows:")
        print(json.dumps([dict(r) for r in rows], ensure_ascii=False, indent=2))
    finally:
        con.close()


if __name__ == "__main__":
    main()
