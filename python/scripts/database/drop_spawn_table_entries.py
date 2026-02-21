"""Drop the legacy 'spawn_table_entries' table from the SQLite database.

Usage (from project root):
    python scripts/drop_spawn_table_entries.py

The script uses the project's SQLAlchemy engine so it targets `data/roguelike.sqlite3`.
It first checks for the table's existence and then executes:
    DROP TABLE IF EXISTS spawn_table_entries;
"""
from __future__ import annotations

from pathlib import Path
import sys
import logging
from typing import Final

from sqlalchemy import text, inspect
from sqlalchemy.engine import Engine

# Ensure src/ is importable when running the script directly
SRC_PATH = Path(__file__).resolve().parents[1] / "src"
if str(SRC_PATH) not in sys.path:
    sys.path.insert(0, str(SRC_PATH))

from roguelike_engine.db.engine import engine  # noqa: E402


LOGGER = logging.getLogger("drop_spawn_table_entries")
logging.basicConfig(level=logging.INFO, format="%(levelname)s - %(message)s")

TABLE_NAME: Final[str] = "spawn_table_entries"


def table_exists(eng: Engine, name: str) -> bool:
    """Return True if the given table exists in the connected database."""
    try:
        insp = inspect(eng)
        return name in insp.get_table_names()
    except Exception:  # pragma: no cover - defensive
        return False


def drop_table_if_exists(eng: Engine, name: str) -> bool:
    """Drop table if it exists. Returns True if a DROP was executed, False otherwise."""
    if not table_exists(eng, name):
        LOGGER.info("Table '%s' does not exist; nothing to do.", name)
        return False
    LOGGER.info("Dropping table '%s'...", name)
    with eng.begin() as conn:
        conn.execute(text(f"DROP TABLE IF EXISTS {name};"))
    LOGGER.info("Table '%s' dropped (or already absent).", name)
    return True


def main() -> None:
    try:
        dropped = drop_table_if_exists(engine, TABLE_NAME)
        if dropped:
            # Verify after drop
            exists = table_exists(engine, TABLE_NAME)
            if exists:
                LOGGER.error("Verification failed: table '%s' still present.", TABLE_NAME)
                sys.exit(2)
            LOGGER.info("Verification OK: table '%s' not found.", TABLE_NAME)
        else:
            LOGGER.info("No action taken.")
    except Exception as exc:  # noqa: BLE001
        LOGGER.exception("Error while dropping table: %s", exc)
        sys.exit(1)


if __name__ == "__main__":
    main()
