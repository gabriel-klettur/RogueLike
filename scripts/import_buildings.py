from __future__ import annotations

"""Import building instances and collisions into SQLite.

- Instances from `data/buildings/buildings_instances.json`.
- Collisions from `data/buildings/buildings_collisions_by_building_instance_id.json`.
- Stores minimal instance metadata into `building_instances` and per-instance
  collision grid as WKT MULTIPOLYGON rows in `building_collisions`.

Assumptions:
- Grid cells marked with `#` are solid; `.` are empty.
- Each solid cell becomes a 1x1 square polygon at integer grid coords.
- We store `kind="tile"` and include original grid metadata in `extra_json`.

Idempotency:
- Composite SHA256 hash across both files is tracked in `import_log`.
- For each instance we clear previous `building_collisions` by `instance_id` then insert.

Run:
    python -m scripts.import_buildings
"""

from dataclasses import dataclass
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
from typing import Any, Dict, Iterable, List

from sqlalchemy import select, delete
from sqlalchemy.dialects.sqlite import insert

# Ensure src/ is importable
import sys
sys.path.append(str(Path(__file__).resolve().parents[1] / "src"))

from roguelike_engine.db.engine import session_scope
from roguelike_engine.db.models import BuildingInstance, BuildingCollision, ImportLog


INSTANCES_PATH = Path("data/buildings/buildings_instances.json")
COLLISIONS_BY_INSTANCE_PATH = Path(
    "data/buildings/buildings_collisions_by_building_instance_id.json"
)


@dataclass
class ImportResult:
    imported: bool
    instances: int
    collisions: int
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


def _multipolygon_from_grid(collision_grid: List[List[str]]) -> str:
    """Produce a MULTIPOLYGON WKT where each '#' cell is a 1x1 square.

    Coordinates are in grid units: cell (x,y) covers [x,x+1] x [y,y+1].
    """
    polys: List[str] = []
    height = len(collision_grid)
    for y, row in enumerate(collision_grid):
        if not isinstance(row, list):
            continue
        for x, cell in enumerate(row):
            if cell == "#":
                # WKT polygon ring; y origin is top-left per file, keep as-is
                ring = f"({x} {y}, {x+1} {y}, {x+1} {y+1}, {x} {y+1}, {x} {y})"
                polys.append(f"(({ring}))")
    if not polys:
        return "MULTIPOLYGON EMPTY"  # empty multipolygon per WKT spec
    return "MULTIPOLYGON(" + ", ".join(polys) + ")"


def import_buildings() -> ImportResult:
    if not INSTANCES_PATH.exists() or not COLLISIONS_BY_INSTANCE_PATH.exists():
        raise SystemExit("Missing buildings JSON files")

    composite_hash = _content_hash_composite([INSTANCES_PATH, COLLISIONS_BY_INSTANCE_PATH])

    instances_json = _json_load(INSTANCES_PATH)
    collisions_by_instance = _json_load(COLLISIONS_BY_INSTANCE_PATH)

    inst_count = 0
    coll_count = 0

    with session_scope() as s:
        last_hash = s.execute(
            select(ImportLog.content_hash)
            .where(ImportLog.source_path == "buildings:composite")
            .order_by(ImportLog.id.desc())
            .limit(1)
        ).scalar_one_or_none()
        if last_hash == composite_hash:
            return ImportResult(False, 0, 0, composite_hash, "unchanged: composite hash matches")

        if not isinstance(instances_json, list):
            raise ValueError("buildings_instances.json must be a list")
        if not isinstance(collisions_by_instance, dict):
            raise ValueError("buildings_collisions_by_building_instance_id.json must be a dict")

        # Import instances (minimal columns per current schema)
        for inst in instances_json:
            instance_id = inst.get("id")
            zone = inst.get("zone")
            if instance_id is None:
                continue
            iid_str = str(instance_id)

            stmt = insert(BuildingInstance).values(
                instance_id=iid_str,
                image_id=None,  # not modeled here; keep None (metadata stays in collisions extra_json)
                spawn_id=None,
                zone_id=str(zone) if zone is not None else None,
            )
            stmt = stmt.on_conflict_do_update(
                index_elements=[BuildingInstance.instance_id],
                set_={
                    "image_id": stmt.excluded.image_id,
                    "spawn_id": stmt.excluded.spawn_id,
                    "zone_id": stmt.excluded.zone_id,
                },
            )
            s.execute(stmt)
            inst_count += 1

            # Collisions for this instance
            c = collisions_by_instance.get(iid_str)
            if isinstance(c, dict):
                grid = c.get("collision")
                if isinstance(grid, list):
                    shape_wkt = _multipolygon_from_grid(grid)
                    # Clear existing collisions for idempotency
                    s.execute(
                        delete(BuildingCollision).where(
                            BuildingCollision.instance_id == iid_str
                        )
                    )
                    s.add(
                        BuildingCollision(
                            instance_id=iid_str,
                            kind="tile",
                            shape_wkt=shape_wkt,
                            extra_json=_json_str({
                                "width": c.get("width"),
                                "height": c.get("height"),
                            }),
                        )
                    )
                    coll_count += 1

        # Log composite import
        s.add(
            ImportLog(
                source_path="buildings:composite",
                content_hash=composite_hash,
                imported_at=datetime.now(timezone.utc).isoformat(),
                row_count=inst_count + coll_count,
                version="buildings_v1",
            )
        )

    return ImportResult(True, inst_count, coll_count, composite_hash, "imported")


def run() -> None:
    res = import_buildings()
    status = "imported" if res.imported else "skipped"
    print(
        f"[buildings] {status} instances={res.instances} collisions={res.collisions} "
        f"hash={res.content_hash} reason={res.reason}"
    )


if __name__ == "__main__":
    run()
