from __future__ import annotations

"""Database engine and session utilities for the Roguelike project.

- Uses SQLite as local development database (single file, no server).
- Exposes `engine`, `SessionLocal`, and a `session_scope()` context manager.
- Enables WAL mode for better read concurrency and sets `synchronous=NORMAL`.

This module is intentionally small and import-safe for Alembic's `env.py`.
"""

from contextlib import contextmanager
from pathlib import Path
from typing import Iterator

from sqlalchemy import create_engine, event
from sqlalchemy.orm import sessionmaker


# Database path: data/roguelike.sqlite3
DB_PATH = Path("data/roguelike.sqlite3")
DB_PATH.parent.mkdir(parents=True, exist_ok=True)

# SQLite URL format: sqlite:///absolute_or_relative_path
DATABASE_URL = f"sqlite:///{DB_PATH.as_posix()}"

# Create the SQLAlchemy engine
engine = create_engine(
    DATABASE_URL,
    future=True,
)


@event.listens_for(engine, "connect")
def _set_sqlite_pragma(dbapi_connection, connection_record) -> None:  # type: ignore[no-untyped-def]
    """Set recommended SQLite PRAGMAs when a new DB-API connection is created.

    - WAL (Write-Ahead Logging) improves read concurrency.
    - synchronous=NORMAL offers a good durability/performance trade-off for games.
    """
    try:
        cursor = dbapi_connection.cursor()
        cursor.execute("PRAGMA journal_mode=WAL;")
        cursor.execute("PRAGMA synchronous=NORMAL;")

        # Best-effort lightweight schema alignment for tests: add missing columns if needed
        try:
            rows = cursor.execute("PRAGMA table_info(entities);").fetchall()
            cols = {str(r[1]).lower() for r in rows}
            if "extra_json" not in cols:
                cursor.execute("ALTER TABLE entities ADD COLUMN extra_json TEXT;")
        except Exception:
            # Ignore on non-SQLite or if table does not exist yet; create_all will handle
            pass

        cursor.close()
    except Exception:
        # Be resilient: don't fail if PRAGMA is unsupported (e.g., non-SQLite)
        pass


# Configure session factory
SessionLocal = sessionmaker(bind=engine, autoflush=False, autocommit=False, future=True)


@contextmanager
def session_scope() -> Iterator:
    """Provide a transactional scope around a series of operations.

    Usage:
        with session_scope() as session:
            session.add(obj)
            ...
    """
    session = SessionLocal()
    try:
        yield session
        session.commit()
    except Exception:
        session.rollback()
        raise
    finally:
        session.close()
