from __future__ import annotations

"""Import spells from JSON into SQLite using SQLAlchemy with upsert semantics.

- Reads `data/spells/spells.json` (dict keyed by spell id).
- Computes content hash (SHA256) to support idempotent imports via `import_log`.
- Upserts into `spells` table based on primary key (`id`).
- Records an entry in `import_log` with row count and timestamp.

Run:
    python -m scripts.import_spells
"""

from dataclasses import dataclass
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
from typing import Any, Dict, Iterable

from sqlalchemy import select, func
from sqlalchemy.dialects.sqlite import insert

# Make 'src/' importable when running as a script (src layout)
import sys
sys.path.append(str(Path(__file__).resolve().parents[1] / "src"))

from roguelike_engine.db.engine import session_scope
from roguelike_engine.db.models import Spell, ImportLog


SRC_PATH = Path("data/spells/spells.json")


@dataclass
class ImportResult:
    imported: bool
    row_count: int
    content_hash: str
    reason: str


def _content_hash(p: Path) -> str:
    return hashlib.sha256(p.read_bytes()).hexdigest()


def _load_spells_dict(p: Path) -> Dict[str, Dict[str, Any]]:
    """Load spells file where top-level is a dict keyed by spell id."""
    raw = json.loads(p.read_text(encoding="utf-8"))
    if not isinstance(raw, dict):
        raise ValueError("Expected a JSON object mapping id -> spell definition")
    return raw


def _cooldown_ms(spell_def: Dict[str, Any]) -> int | None:
    timings = spell_def.get("timings")
    if not isinstance(timings, dict):
        return None
    cd = timings.get("cooldown")
    if isinstance(cd, (int, float)):
        return int(cd * 1000)
    return None


def _json_str(obj: Any) -> str:
    return json.dumps(obj, ensure_ascii=False, separators=(",", ":"))


def _should_skip_import(s, source_path: str, new_hash: str) -> bool:
    """Return True if the last import for this source has the same content hash."""
    stmt = (
        select(ImportLog.content_hash)
        .where(ImportLog.source_path == source_path)
        .order_by(ImportLog.id.desc())
        .limit(1)
    )
    last = s.execute(stmt).scalar_one_or_none()
    return last == new_hash


def import_spells() -> ImportResult:
    if not SRC_PATH.exists():
        raise SystemExit(f"Missing {SRC_PATH}")

    h = _content_hash(SRC_PATH)
    data = _load_spells_dict(SRC_PATH)

    row_counter = 0
    with session_scope() as s:
        if _should_skip_import(s, str(SRC_PATH), h):
            return ImportResult(False, 0, h, "unchanged: content hash matches latest import")

        for spell_id, item in data.items():
            # Normalize fields to our schema; keep full JSON in extra_json
            stmt = insert(Spell).values(
                id=item.get("id", spell_id),
                name=item.get("name", spell_id),
                type=item.get("type"),
                element=item.get("element"),
                mana_cost=item.get("mana_cost"),
                cooldown_ms=_cooldown_ms(item),
                tags=None,  # no stable tags in source yet
                extra_json=_json_str(item),
            )
            stmt = stmt.on_conflict_do_update(
                index_elements=[Spell.id],
                set_={
                    "name": stmt.excluded.name,
                    "type": stmt.excluded.type,
                    "element": stmt.excluded.element,
                    "mana_cost": stmt.excluded.mana_cost,
                    "cooldown_ms": stmt.excluded.cooldown_ms,
                    "tags": stmt.excluded.tags,
                    "extra_json": stmt.excluded.extra_json,
                },
            )
            s.execute(stmt)
            row_counter += 1

        # Record import log entry
        s.add(
            ImportLog(
                source_path=str(SRC_PATH),
                content_hash=h,
                imported_at=datetime.now(timezone.utc).isoformat(),
                row_count=row_counter,
                version="spells_v1",
            )
        )

    return ImportResult(True, row_counter, h, "imported")


def run() -> None:
    res = import_spells()
    status = "imported" if res.imported else "skipped"
    print(f"[spells] {status} rows={res.row_count} hash={res.content_hash} reason={res.reason}")


if __name__ == "__main__":
    run()
