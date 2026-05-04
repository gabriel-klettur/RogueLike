"""Export the Python `items` + `item_prices` SQLite tables to a stable JSON file.

The Unity migrator (`PythonDataMigrator.Items.cs`) reads this file to generate
ItemDefinition ScriptableObjects. Keeping the export as JSON (instead of having
Unity read SQLite directly) means:

  * No native SQLite dependency in the Unity assemblies.
  * The export is plain text, diffable, and can live in git.
  * Re-running the script after any DB edit produces a deterministic snapshot.

Run from the repo root:

    python python/scripts/export_items_to_json.py

Output: python/data/items/items_export.json
"""

from __future__ import annotations

import json
import sqlite3
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
DB_PATH = REPO_ROOT / "python" / "data" / "roguelike.sqlite3"
OUT_PATH = REPO_ROOT / "python" / "data" / "items" / "items_export.json"


def main() -> int:
    if not DB_PATH.exists():
        print(f"[export_items] SQLite DB not found at {DB_PATH}", file=sys.stderr)
        return 1

    con = sqlite3.connect(str(DB_PATH))
    con.row_factory = sqlite3.Row
    cur = con.cursor()

    cur.execute("PRAGMA table_info(items)")
    item_cols = [row[1] for row in cur.fetchall()]
    if not item_cols:
        print("[export_items] 'items' table is empty or missing", file=sys.stderr)
        return 1

    cur.execute(
        """
        SELECT i.*, p.buy_price AS buy_price, p.sell_price AS sell_price
          FROM items i
     LEFT JOIN item_prices p ON i.id = p.id_item
      ORDER BY i.id
        """
    )
    rows = [dict(r) for r in cur.fetchall()]

    # Parse icon_json (TEXT) into a real list when present so Unity doesn't
    # have to do nested JSON parsing.
    for row in rows:
        raw = row.get("icon_json")
        if isinstance(raw, str) and raw.strip():
            try:
                row["icon_json"] = json.loads(raw)
            except json.JSONDecodeError:
                # Keep the raw string so the migrator can warn instead of silently dropping data.
                pass

    payload = {
        "schema_version": 1,
        "source": "python/data/roguelike.sqlite3 :: items + item_prices",
        "count": len(rows),
        "items": rows,
    }

    OUT_PATH.parent.mkdir(parents=True, exist_ok=True)
    OUT_PATH.write_text(
        json.dumps(payload, indent=2, ensure_ascii=False, sort_keys=False),
        encoding="utf-8",
    )

    print(f"[export_items] Wrote {len(rows)} items to {OUT_PATH.relative_to(REPO_ROOT)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
