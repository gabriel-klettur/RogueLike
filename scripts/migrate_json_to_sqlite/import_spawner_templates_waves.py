from __future__ import annotations

"""Import spawner templates and waves from JSON into SQLite.

- Templates from data/spawners/spawners_templates.json
- Waves from data/spawners/spawners_waves.json
- Stores templates into `spawner_templates` and waves into `spawner_waves`.
- Idempotent via composite SHA256 of both files logged in `import_log`.

Run:
    python scripts/migrate_json_to_sqlite/import_spawner_templates_waves.py
"""

from dataclasses import dataclass
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
from typing import Any, Dict, Iterable, List

from sqlalchemy.dialects.sqlite import insert
from sqlalchemy import select, delete

# Ensure src/ is importable (repo_root/src)
import sys
sys.path.append(str(Path(__file__).resolve().parents[2] / "src"))

from roguelike_engine.db.engine import session_scope
from roguelike_engine.db.models import SpawnerTemplate, SpawnerWaves, ImportLog

TEMPLATES_PATH = Path("data/spawners/spawners_templates.json")
WAVES_PATH = Path("data/spawners/spawners_waves.json")


@dataclass
class ImportResult:
    imported: bool
    templates: int
    waves_rows: int
    content_hash: str
    reason: str


def _content_hash_composite(paths: Iterable[Path]) -> str:
    h = hashlib.sha256()
    for p in paths:
        if p.exists():
            h.update(p.read_bytes())
        else:
            h.update(b"<missing>")
    return h.hexdigest()


def _json_load(p: Path) -> Any:
    return json.loads(p.read_text(encoding="utf-8"))


def _json_str(obj: Any) -> str:
    return json.dumps(obj, ensure_ascii=False, separators=(",", ":"))


def _as_str(val: Any) -> str | None:
    if val is None:
        return None
    if isinstance(val, (str, int, float, bool)):
        return str(val)
    return _json_str(val)


def import_templates_waves() -> ImportResult:
    if not TEMPLATES_PATH.exists():
        raise SystemExit("Missing spawners_templates.json")

    composite_hash = _content_hash_composite([TEMPLATES_PATH, WAVES_PATH])

    templates_list = _json_load(TEMPLATES_PATH)
    waves_catalog = _json_load(WAVES_PATH) if WAVES_PATH.exists() else {}

    t_count = 0
    w_rows = 0

    with session_scope() as s:
        last_hash = s.execute(
            select(ImportLog.content_hash)
            .where(ImportLog.source_path == "spawner_templates_waves:composite")
            .order_by(ImportLog.id.desc())
            .limit(1)
        ).scalar_one_or_none()
        if last_hash == composite_hash:
            return ImportResult(False, 0, 0, composite_hash, "unchanged: composite hash matches")

        # Templates
        if not isinstance(templates_list, list):
            raise ValueError("spawners_templates.json must be a list")
        for t in templates_list:
            tid = t.get("id")
            if not isinstance(tid, str):
                continue
            stmt = insert(SpawnerTemplate).values(
                id=tid,
                spawner_type=t.get("spawner_type"),
                spawner_shape=t.get("spawner_shape"),
                spawn_radius_text=_as_str(t.get("spawn_radius")),
                defend_spawn=t.get("defend_spawn"),
                defend_leash=t.get("defend_leash"),
                visible_in_game=t.get("visible_in_game"),
                trigger_json=_json_str(t.get("trigger")) if t.get("trigger") is not None else None,
                policy_json=_json_str(t.get("policy")) if t.get("policy") is not None else None,
                waves_id=t.get("waves_id"),
            )
            stmt = stmt.on_conflict_do_update(
                index_elements=[SpawnerTemplate.id],
                set_={
                    "spawner_type": stmt.excluded.spawner_type,
                    "spawner_shape": stmt.excluded.spawner_shape,
                    "spawn_radius_text": stmt.excluded.spawn_radius_text,
                    "defend_spawn": stmt.excluded.defend_spawn,
                    "defend_leash": stmt.excluded.defend_leash,
                    "visible_in_game": stmt.excluded.visible_in_game,
                    "trigger_json": stmt.excluded.trigger_json,
                    "policy_json": stmt.excluded.policy_json,
                    "waves_id": stmt.excluded.waves_id,
                },
            )
            s.execute(stmt)
            t_count += 1

        # Waves
        if isinstance(waves_catalog, dict):
            for waves_id, sequence in waves_catalog.items():
                if not isinstance(sequence, list):
                    continue
                # Clear existing rows for this waves_id
                s.execute(delete(SpawnerWaves).where(SpawnerWaves.waves_id == str(waves_id)))
                for idx, wave in enumerate(sequence):
                    spawns = wave.get("spawns") if isinstance(wave, dict) else wave
                    s.execute(
                        insert(SpawnerWaves).values(
                            waves_id=str(waves_id),
                            idx=int(idx),
                            spawns_json=_json_str(spawns),
                        )
                    )
                    w_rows += 1

        # Log composite import
        s.add(
            ImportLog(
                source_path="spawner_templates_waves:composite",
                content_hash=composite_hash,
                imported_at=datetime.now(timezone.utc).isoformat(),
                row_count=t_count + w_rows,
                version="spawner_templates_waves_v1",
            )
        )

    return ImportResult(True, t_count, w_rows, composite_hash, "imported")


def run() -> None:
    res = import_templates_waves()
    status = "imported" if res.imported else "skipped"
    print(
        f"[spawner_templates_waves] {status} templates={res.templates} waves_rows={res.waves_rows} "
        f"hash={res.content_hash} reason={res.reason}"
    )


if __name__ == "__main__":
    run()
