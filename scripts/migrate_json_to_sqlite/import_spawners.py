from __future__ import annotations

"""Import spawners (instances + templates + waves) into SQLite.

Mappings:
- Spawner rows come from `data/spawners/spawners_instances.json` joined with
  `data/spawners/spawners_templates.json` (by `template_id`), optionally with
  `data/spawners/spawners_waves.json` when the template has `waves_id`.
- We store:
  - `Spawner.id`           <- instance.id (string)
  - `Spawner.map_id`       <- instance.zone
  - `Spawner.x, y`         <- instance.tile[0], tile[1]
  - `Spawner.radius`       <- template.spawn_radius (int) or None
  - `Spawner.respawn_seconds` <- template.policy.cooldown_s (int)
  - `Spawner.conditions_json` <- JSON dump of trigger/policy/overrides relevant bits
  - `Spawner.spawn_table_id`  <- instance.template_id (string)
  Note: We no longer materialize spawn entries; consumers should resolve
  spawns at query time via `spawner_templates.waves_id` and `spawner_waves`.

Idempotency:
- Composite content hash built from the three JSON files.

Run:
    python -m scripts.import_spawners
"""

from dataclasses import dataclass
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
from typing import Any, Dict, Iterable, List

from sqlalchemy.dialects.sqlite import insert
from sqlalchemy import select

# Ensure src/ is importable (repo_root/src)
import sys
sys.path.append(str(Path(__file__).resolve().parents[2] / "src"))

from roguelike_engine.db.engine import session_scope
from roguelike_engine.db.models import SpawnerInstance, ImportLog


INSTANCES_PATH = Path("data/spawners/spawners_instances.json")
TEMPLATES_PATH = Path("data/spawners/spawners_templates.json")
WAVES_PATH = Path("data/spawners/spawners_waves.json")


@dataclass
class ImportResult:
    imported: bool
    row_count_spawners: int
    content_hash: str
    reason: str


def _content_hash_file(p: Path) -> str:
    return hashlib.sha256(p.read_bytes()).hexdigest()


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


def _num(n: Any) -> int | None:
    if isinstance(n, (int, float)):
        return int(n)
    return None


def _radius_value(val: Any) -> int | None:
    # allow numbers only; ignore strings like "random" for now
    return _num(val)


def _waves_for_template(template: Dict[str, Any], waves_catalog: Dict[str, Any]) -> List[Dict[str, Any]]:
    """Return waves list for a template (inline or via waves_id);
    kept for potential future validation or runtime utilities.
    """
    if "waves" in template and isinstance(template["waves"], list):
        return template["waves"]
    waves_id = template.get("waves_id")
    if isinstance(waves_id, str):
        seq = (waves_catalog or {}).get(waves_id)
        if isinstance(seq, list):
            return seq
    return []


def import_spawners() -> ImportResult:
    if not INSTANCES_PATH.exists() or not TEMPLATES_PATH.exists():
        raise SystemExit("Missing spawners JSON files")

    composite_hash = _content_hash_composite([INSTANCES_PATH, TEMPLATES_PATH, WAVES_PATH])

    instances = _json_load(INSTANCES_PATH)
    templates_list = _json_load(TEMPLATES_PATH)
    waves_catalog = _json_load(WAVES_PATH) if WAVES_PATH.exists() else {}

    # Index templates by id
    templates: Dict[str, Dict[str, Any]] = {}
    if isinstance(templates_list, list):
        for t in templates_list:
            tid = t.get("id")
            if isinstance(tid, str):
                templates[tid] = t

    sp_count = 0
    # entries are no longer materialized; only count spawner instances

    with session_scope() as s:
        # Idempotency on the composite input set
        last_hash = s.execute(
            select(ImportLog.content_hash)
            .where(ImportLog.source_path == "spawners_instances:composite")
            .order_by(ImportLog.id.desc())
            .limit(1)
        ).scalar_one_or_none()
        if last_hash == composite_hash:
            return ImportResult(False, 0, composite_hash, "unchanged: composite hash matches")

        if not isinstance(instances, list):
            raise ValueError("spawners_instances.json must be a list")

        for inst in instances:
            sp_id = inst.get("id")
            if not isinstance(sp_id, str):
                continue
            template_id = inst.get("template_id")
            t = templates.get(template_id) if isinstance(template_id, str) else None
            if not isinstance(t, dict):
                # Skip if template is missing
                continue

            tile = inst.get("tile") or [None, None]
            x = _num(tile[0]) if isinstance(tile, list) and len(tile) >= 2 else None
            y = _num(tile[1]) if isinstance(tile, list) and len(tile) >= 2 else None

            trigger = t.get("trigger") or {}
            policy = t.get("policy") or {}
            radius = _radius_value(t.get("spawn_radius"))
            cooldown = _num(policy.get("cooldown_s"))

            cond = {
                "trigger": trigger,
                "policy": policy,
                "overrides": inst.get("overrides"),
                "visuals": inst.get("visuals"),
            }

            # Upsert SpawnerInstance
            stmt = insert(SpawnerInstance).values(
                id=sp_id,
                map_id=inst.get("zone"),
                x=x,
                y=y,
                radius=radius,
                max_count=None,
                respawn_seconds=cooldown,
                conditions_json=_json_str(cond),
                spawn_table_id=str(template_id) if isinstance(template_id, str) else None,
            )
            stmt = stmt.on_conflict_do_update(
                index_elements=[SpawnerInstance.id],
                set_={
                    "map_id": stmt.excluded.map_id,
                    "x": stmt.excluded.x,
                    "y": stmt.excluded.y,
                    "radius": stmt.excluded.radius,
                    "max_count": stmt.excluded.max_count,
                    "respawn_seconds": stmt.excluded.respawn_seconds,
                    "conditions_json": stmt.excluded.conditions_json,
                    "spawn_table_id": stmt.excluded.spawn_table_id,
                },
            )
            s.execute(stmt)
            sp_count += 1

            # Previously we materialized entries into spawn_table_entries.
            # Now we rely on spawner_templates.waves_id + spawner_waves at query time.

        # Log composite import
        s.add(
            ImportLog(
                source_path="spawners_instances:composite",
                content_hash=composite_hash,
                imported_at=datetime.now(timezone.utc).isoformat(),
                row_count=sp_count,
                version="spawners_v1",
            )
        )

    return ImportResult(True, sp_count, composite_hash, "imported")


def run() -> None:
    res = import_spawners()
    status = "imported" if res.imported else "skipped"
    print(
        f"[spawners_instances] {status} instances={res.row_count_spawners} "
        f"hash={res.content_hash} reason={res.reason}"
    )


if __name__ == "__main__":
    run()
