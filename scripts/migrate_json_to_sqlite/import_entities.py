from __future__ import annotations

"""Import entities (hostiles, neutrals, players) from JSON into SQLite.

- Supports multiple JSON sources under data/entities/.
- Normalizes common stats into columns; preserves full JSON in `extra_json`.
- Idempotent via per-file SHA256 and `import_log`.

Run:
    python -m scripts.import_entities
"""

from dataclasses import dataclass
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
from typing import Any, Dict, Iterable, Iterator, Tuple

from sqlalchemy.dialects.sqlite import insert
from sqlalchemy import select

# Ensure src/ is importable
import sys
sys.path.append(str(Path(__file__).resolve().parents[1] / "src"))

from roguelike_engine.db.engine import session_scope
from roguelike_engine.db.models import Entity, ImportLog


BASE_DIR = Path("data/entities")
SOURCES = [
    BASE_DIR / "new_hostiles.json",
    BASE_DIR / "new_neutrals.json",
    BASE_DIR / "new_players.json",
]


@dataclass
class ImportOutcome:
    source_path: Path
    imported: bool
    row_count: int
    content_hash: str
    reason: str


def _content_hash(p: Path) -> str:
    return hashlib.sha256(p.read_bytes()).hexdigest()


def _json_load(p: Path) -> Any:
    return json.loads(p.read_text(encoding="utf-8"))


def _json_str(obj: Any) -> str:
    return json.dumps(obj, ensure_ascii=False, separators=(",", ":"))


def _should_skip(s, source_path: str, h: str) -> bool:
    stmt = (
        select(ImportLog.content_hash)
        .where(ImportLog.source_path == source_path)
        .order_by(ImportLog.id.desc())
        .limit(1)
    )
    last = s.execute(stmt).scalar_one_or_none()
    return last == h


def _iter_hostile_classes(doc: Dict[str, Any]) -> Iterator[Tuple[str, Dict[str, Any]]]:
    classes = (doc.get("hostiles") or {}).get("classes", {})
    if isinstance(classes, dict):
        for k, v in classes.items():
            yield k, v


def _iter_neutral_classes(doc: Dict[str, Any]) -> Iterator[Tuple[str, Dict[str, Any]]]:
    classes = (doc.get("neutrals") or {}).get("classes", {})
    if isinstance(classes, dict):
        for k, v in classes.items():
            yield k, v


def _iter_player_classes(doc: Dict[str, Any]) -> Iterator[Tuple[str, Dict[str, Any]]]:
    classes = (doc.get("players") or {}).get("classes", {})
    if isinstance(classes, dict):
        for k, v in classes.items():
            yield k, v


def _int_or_none(x: Any) -> int | None:
    return int(x) if isinstance(x, (int, float)) else None


def _float_or_none(x: Any) -> float | None:
    return float(x) if isinstance(x, (int, float)) else None


def _insert_entity_stmt(kind: str, key: str, data: Dict[str, Any]):
    stats = data.get("stats") or {}
    name = data.get("default_name") or key
    # Map common stats; fallbacks to None
    if kind == "player":
        speed = _float_or_none(stats.get("basic_speed"))
        atk = _int_or_none(stats.get("basic_attack"))
        defense = _int_or_none(stats.get("basic_armor"))
        hp = None
    else:
        speed = _float_or_none(stats.get("speed"))
        atk = _int_or_none(stats.get("melee_damage"))
        defense = _int_or_none(stats.get("defense"))
        hp = _int_or_none(stats.get("hp"))

    stmt = insert(Entity).values(
        id=key,
        kind=kind,
        name=name,
        level=None,
        hp=hp,
        atk=atk,
        **{"def": defense},  # use column name 'def'
        speed=speed,
        ai_behavior=data.get("fsm_set"),
        loot_table_id=None,
        extra_json=_json_str(data),
    )
    stmt = stmt.on_conflict_do_update(
        index_elements=[Entity.id],
        set_={
            "kind": stmt.excluded.kind,
            "name": stmt.excluded.name,
            "level": stmt.excluded.level,
            "hp": stmt.excluded.hp,
            "atk": stmt.excluded.atk,
            "def": stmt.excluded.__getattr__("def"),
            "speed": stmt.excluded.speed,
            "ai_behavior": stmt.excluded.ai_behavior,
            "loot_table_id": stmt.excluded.loot_table_id,
            "extra_json": stmt.excluded.extra_json,
        },
    )
    return stmt


def import_one(source: Path) -> ImportOutcome:
    if not source.exists():
        return ImportOutcome(source, False, 0, "", "missing")

    h = _content_hash(source)
    doc = _json_load(source)

    kind = None
    iters: list[tuple[str, Iterator[Tuple[str, Dict[str, Any]]]]] = []
    if source.name.startswith("new_hostiles"):
        kind = "hostile"
        iters.append(("hostile", _iter_hostile_classes(doc)))
    elif source.name.startswith("new_neutrals"):
        kind = "neutral"
        iters.append(("neutral", _iter_neutral_classes(doc)))
    elif source.name.startswith("new_players"):
        kind = "player"
        iters.append(("player", _iter_player_classes(doc)))

    row_count = 0
    with session_scope() as s:
        if _should_skip(s, str(source), h):
            return ImportOutcome(source, False, 0, h, "unchanged: hash matches")

        for k, iterator in iters:
            for ent_id, payload in iterator:
                s.execute(_insert_entity_stmt(k, ent_id, payload))
                row_count += 1

        s.add(
            ImportLog(
                source_path=str(source),
                content_hash=h,
                imported_at=datetime.now(timezone.utc).isoformat(),
                row_count=row_count,
                version="entities_v1",
            )
        )

    return ImportOutcome(source, True, row_count, h, "imported")


def run() -> None:
    total = 0
    for src in SOURCES:
        outcome = import_one(src)
        status = "imported" if outcome.imported else "skipped"
        print(f"[entities] {src.name}: {status} rows={outcome.row_count} hash={outcome.content_hash} reason={outcome.reason}")
        total += outcome.row_count
    print(f"[entities] total rows processed: {total}")


if __name__ == "__main__":
    run()
