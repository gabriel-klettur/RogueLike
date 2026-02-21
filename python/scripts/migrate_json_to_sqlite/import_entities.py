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
from sqlalchemy import select, delete

# Ensure src/ is importable
import sys
sys.path.append(str(Path(__file__).resolve().parents[1] / "src"))

from roguelike_engine.db.engine import session_scope
from roguelike_engine.db.models import (
    Entity,
    ImportLog,
    EntityAssetSet,
    EntityAssetNoSet,
    EntityPayloadArchive,
)


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


def _str_hash(s: str) -> str:
    return hashlib.sha256(s.encode("utf-8")).hexdigest()


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

    # Flattened fields
    def _get(d: Dict[str, Any] | None, *path: str):
        cur = d or {}
        for k in path:
            cur = cur.get(k) if isinstance(cur, dict) else None
        return cur

    patrol = data.get("patrol") or {}
    patrol_params = _get(patrol, "params") or {}

    assets = data.get("assets") or {}
    sets_data = _get(assets, "sets", "sprites_data_set") or {}
    no_sets = assets.get("no-sets") or {}
    no_sets_data = _get(no_sets, "sprites_data_no-set") or {}

    def _tint_rgb(v):
        if isinstance(v, (list, tuple)) and len(v) == 3:
            return _int_or_none(v[0]), _int_or_none(v[1]), _int_or_none(v[2])
        return None, None, None

    fields: Dict[str, Any] = {
        "id": key,
        "kind": kind,
        "name": name,
        "level": None,
        "hp": hp,
        "atk": atk,
        "def": defense,
        "speed": speed,
        "ai_behavior": data.get("fsm_set"),
        "loot_table_id": None,
        # stats common
        "faction": stats.get("faction"),
        "aggro_range": _int_or_none(stats.get("aggro_range")),
        "melee_range": _int_or_none(stats.get("melee_range")),
        "melee_damage": _int_or_none(stats.get("melee_damage")),
        "melee_cooldown": _float_or_none(stats.get("melee_cooldown")),
        "power": _int_or_none(stats.get("power")),
        "damage_duration": _float_or_none(stats.get("damage_duration")),
        "chasing_speed": _float_or_none(stats.get("chasing_speed")),
        "feet_width_factor": _float_or_none(stats.get("feet_width_factor")),
        "feet_height_factor": _float_or_none(stats.get("feet_height_factor")),
        "spawn_padding": _int_or_none(stats.get("spawn_padding")),
        "spawn_count": _int_or_none(stats.get("spawn_count")),
        "spawn_margin": _int_or_none(stats.get("spawn_margin")),
        "death_dissapear_time": _float_or_none(stats.get("death_dissapear_time")),
        "damage_stop_probability": _float_or_none(stats.get("damage_stop_probability")),
        "chat_range": _int_or_none(stats.get("chat_range")),
        # player-specific
        "max_strength": _int_or_none(stats.get("max_strength")),
        "max_intelligence": _int_or_none(stats.get("max_intelligence")),
        "max_dexterity": _int_or_none(stats.get("max_dexterity")),
        "initial_strength": _int_or_none(stats.get("initial_strength")),
        "initial_intelligence": _int_or_none(stats.get("initial_intelligence")),
        "initial_dexterity": _int_or_none(stats.get("initial_dexterity")),
        "basic_speed": _float_or_none(stats.get("basic_speed")),
        "basic_attack": _int_or_none(stats.get("basic_attack")),
        "basic_armor": _int_or_none(stats.get("basic_armor")),
        "basic_death_timer_duration": _float_or_none(stats.get("basic_death_timer_duration")),
        "drag_drop_range": _int_or_none(stats.get("drag_drop_range")),
        "dash_charges": _int_or_none(stats.get("dash_charges")),
        "mana_regen_per_second": _float_or_none(stats.get("mana_regen_per_second")),
        "attack_duration": _float_or_none(stats.get("attack_duration")),
        "trail_interval": _float_or_none(_get(stats, "basic_trail", "interval")),
        "trail_life_time": _float_or_none(_get(stats, "basic_trail", "life_time")),
        "trail_max_trails": _int_or_none(_get(stats, "basic_trail", "max_trails")),
        # patrol
        "patrol_id": patrol.get("id"),
        "patrol_radius_tiles": _int_or_none(patrol_params.get("radius_tiles")),
        "patrol_points": _int_or_none(patrol_params.get("points")),
        "patrol_clockwise": bool(patrol_params.get("clockwise")) if patrol_params.get("clockwise") is not None else None,
        "patrol_width_tiles": _int_or_none(patrol_params.get("width_tiles")),
        "patrol_height_tiles": _int_or_none(patrol_params.get("height_tiles")),
        "patrol_points_per_edge": _int_or_none(patrol_params.get("points_per_edge")),
        "patrol_segments": _int_or_none(patrol_params.get("segments")),
        "patrol_step_tiles": _int_or_none(patrol_params.get("step_tiles")),
        "patrol_amplitude_tiles": _int_or_none(patrol_params.get("amplitude_tiles")),
        "patrol_axis": patrol_params.get("axis"),
    }

    # Note: Assets (sets/no-sets) are now stored only in child tables.
    # We keep full payload in extra_json.

    stmt = insert(Entity).values(**fields)
    stmt = stmt.on_conflict_do_update(
        index_elements=[Entity.id],
        set_={k: getattr(stmt.excluded, k) for k in fields.keys() if k not in ("id",)},
    )
    return stmt


def _build_asset_rows(entity_id: str, data: Dict[str, Any]) -> tuple[list[Dict[str, Any]], list[Dict[str, Any]]]:
    """Build rows for EntityAssetSet and EntityAssetNoSet from payload assets.

    - Handles alias action 'cast' -> 'casting'.
    - Normalizes direction synonyms to canonical: s,se,e,ne,n,nw,w,sw.
    """
    assets = data.get("assets") or {}
    sets = (assets.get("sets") or {})
    sprites_set = sets.get("sprites_set") or {}
    sets_data = sets.get("sprites_data_set") or {}

    no_sets = assets.get("no-sets") or {}
    no_sets_data = no_sets.get("sprites_data_no-set") or {}

    def _float(x):
        return float(x) if isinstance(x, (int, float)) else None

    def _tint(v):
        if isinstance(v, (list, tuple)) and len(v) == 3:
            a, b, c = v
            return (
                int(a) if isinstance(a, (int, float)) else None,
                int(b) if isinstance(b, (int, float)) else None,
                int(c) if isinstance(c, (int, float)) else None,
            )
        return (None, None, None)

    DIR_MAP = {
        "north": "n",
        "south": "s",
        "east": "e",
        "west": "w",
        "northeast": "ne",
        "northwest": "nw",
        "southeast": "se",
        "southwest": "sw",
    }

    def _canon_dir(k: str | None) -> str | None:
        if not k:
            return None
        k2 = k.lower()
        if k2 in {"s", "se", "e", "ne", "n", "nw", "w", "sw"}:
            return k2
        return DIR_MAP.get(k2)

    s_tr, s_tg, s_tb = _tint(sets_data.get("tint"))
    n_tr, n_tg, n_tb = _tint(no_sets_data.get("tint"))

    set_rows: list[Dict[str, Any]] = []
    for action, items in (sprites_set.items() if isinstance(sprites_set, dict) else []):
        act = "casting" if action == "cast" else action
        scale = _float(sets_data.get(f"scale_{act}"))
        if isinstance(items, list):
            for idx, path in enumerate(items):
                set_rows.append(
                    {
                        "entity_id": entity_id,
                        "action": act,
                        "direction": None,
                        "idx": idx,
                        "path": path,
                        "scale": scale,
                        "tint_r": s_tr,
                        "tint_g": s_tg,
                        "tint_b": s_tb,
                    }
                )

    no_set_rows: list[Dict[str, Any]] = []
    for action, obj in (no_sets.items() if isinstance(no_sets, dict) else []):
        if action == "sprites_data_no-set":
            continue
        act = "casting" if action == "cast" else action
        scale = _float(no_sets_data.get(f"scale_{act}"))
        if isinstance(obj, dict):
            for dir_key, path in obj.items():
                d = _canon_dir(dir_key)
                if d is None:
                    continue
                no_set_rows.append(
                    {
                        "entity_id": entity_id,
                        "action": act,
                        "direction": d,
                        "path": path,
                        "scale": scale,
                        "tint_r": n_tr,
                        "tint_g": n_tg,
                        "tint_b": n_tb,
                    }
                )

    return set_rows, no_set_rows


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
                # Upsert main entity row
                s.execute(_insert_entity_stmt(k, ent_id, payload))

                # Rebuild asset rows (idempotent: delete then insert)
                s.execute(delete(EntityAssetSet).where(EntityAssetSet.entity_id == ent_id))
                s.execute(delete(EntityAssetNoSet).where(EntityAssetNoSet.entity_id == ent_id))
                set_rows, no_set_rows = _build_asset_rows(ent_id, payload)
                if set_rows:
                    s.execute(insert(EntityAssetSet), set_rows)
                if no_set_rows:
                    s.execute(insert(EntityAssetNoSet), no_set_rows)

                # Upsert full payload into archive for safety/tracking
                payload_json = _json_str(payload)
                stmt_arch = (
                    insert(EntityPayloadArchive)
                    .values(
                        entity_id=ent_id,
                        extra_json=payload_json,
                        content_hash=_str_hash(payload_json),
                        imported_at=datetime.now(timezone.utc).isoformat(),
                    )
                    .on_conflict_do_update(
                        index_elements=[EntityPayloadArchive.entity_id],
                        set_={
                            "extra_json": payload_json,
                            "content_hash": _str_hash(payload_json),
                            "imported_at": datetime.now(timezone.utc).isoformat(),
                        },
                    )
                )
                s.execute(stmt_arch)
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
