from __future__ import annotations

from typing import Any, Dict, List, Optional
import json
import os
import logging

from roguelike_engine.config.config import LIGHT_INSTANCES_PATH, LIGHT_PRESETS_PATH
from roguelike_editors.buildings.utils.zone_helpers import detect_zone_from_px
from roguelike_engine.config.config_tiles import TILE_SIZE

_log = logging.getLogger(__name__)


def _read_json(path: str, default):
    try:
        with open(path, "r", encoding="utf-8") as f:
            return json.load(f)
    except FileNotFoundError:
        return default
    except Exception:
        return default


def load_light_instances() -> List[Dict[str, Any]]:
    data = _read_json(LIGHT_INSTANCES_PATH, [])
    return data if isinstance(data, list) else []


def write_light_instances(data: List[Dict[str, Any]]) -> None:
    os.makedirs(os.path.dirname(LIGHT_INSTANCES_PATH), exist_ok=True)
    # Deduplicate by (zone, rel_x, rel_y, preset_id) + overrides signature
    try:
        def _key(e: Dict[str, Any]) -> str:
            try:
                zone = str(e.get("zone") or "no zone")
                rx = int(e.get("rel_x") or 0)
                ry = int(e.get("rel_y") or 0)
                pid = str(e.get("preset_id") or "")
                ov = e.get("overrides") if isinstance(e, dict) else None
                ovk = json.dumps(ov, sort_keys=True, ensure_ascii=False) if isinstance(ov, dict) else "{}"
                return f"{zone}|{rx}|{ry}|{pid}|{ovk}"
            except Exception:
                return repr(e)
        seen: Dict[str, Dict[str, Any]] = {}
        for e in list(data or []):
            k = _key(e)
            if k not in seen:
                seen[k] = e
        data = list(seen.values())
        # Stable sort by id if present
        try:
            data.sort(key=lambda x: int(x.get("id") or 0))
        except Exception:
            pass
    except Exception:
        data = list(data or [])

    with open(LIGHT_INSTANCES_PATH, "w", encoding="utf-8") as f:
        json.dump(data or [], f, ensure_ascii=False, indent=2)


def _load_presets() -> Dict[str, Dict[str, Any]]:
    raw = _read_json(LIGHT_PRESETS_PATH, {})
    presets = raw.get("presets") if isinstance(raw, dict) else None
    return presets if isinstance(presets, dict) else {}


def _compute_overrides(preset_id: str, params: Dict[str, Any]) -> Dict[str, Any] | None:
    presets = _load_presets()
    base = presets.get(preset_id) if isinstance(presets, dict) else None
    if not isinstance(base, dict):
        return dict(params)
    ov: Dict[str, Any] = {}
    for k in ("radius","intensity","falloff","color","flicker_amp","flicker_speed","center_scale","enabled"):
        if k in params and params[k] != base.get(k):
            ov[k] = params[k]
    return ov or None


def append_instance(preset_id: str, world_x: float, world_y: float, *, params: Optional[Dict[str, Any]] = None) -> Dict[str, Any]:
    """Persist a light instance. Computes zone and relative pixel coords.

    params: current light parameters (used to compute overrides vs preset).
    """
    data = load_light_instances() or []
    # Compute zone and relative px from world px
    zone, (off_tx, off_ty) = detect_zone_from_px(world_x, world_y)
    origin_px_x = int(off_tx) * TILE_SIZE
    origin_px_y = int(off_ty) * TILE_SIZE
    rel_x = int(world_x - origin_px_x)
    rel_y = int(world_y - origin_px_y)
    # Next id
    try:
        next_id = 1 + max((int(e.get("id")) for e in data if isinstance(e, dict) and e.get("id") is not None), default=0)
    except Exception:
        next_id = len(data) + 1
    ov = _compute_overrides(preset_id, params or {})
    entry: Dict[str, Any] = {
        "id": int(next_id),
        "preset_id": str(preset_id),
        "zone": str(zone),
        "rel_x": int(rel_x),
        "rel_y": int(rel_y),
    }
    if ov:
        entry["overrides"] = ov
    data.append(entry)
    write_light_instances(data)
    _log.info(f"[LightInstances] Added id={next_id} preset={preset_id} zone={zone} rel=({rel_x},{rel_y}) ov={bool(ov)}")
    return entry


def get_instance_by_id(inst_id: int) -> Optional[Dict[str, Any]]:
    try:
        data = load_light_instances() or []
        for e in data:
            try:
                if int(e.get('id')) == int(inst_id):
                    return e
            except Exception:
                continue
    except Exception:
        return None
    return None


def update_instance_position(inst_id: int, world_x: float, world_y: float) -> Optional[Dict[str, Any]]:
    """Update zone and relative pixel coords of an instance by id, given world px.

    Returns the updated entry or None if not found.
    """
    try:
        data = load_light_instances() or []
        # Compute zone and rel from world
        zone, (off_tx, off_ty) = detect_zone_from_px(world_x, world_y)
        origin_px_x = int(off_tx) * TILE_SIZE
        origin_px_y = int(off_ty) * TILE_SIZE
        rel_x = int(world_x - origin_px_x)
        rel_y = int(world_y - origin_px_y)
        found = None
        for e in data:
            try:
                if int(e.get('id')) == int(inst_id):
                    e['zone'] = str(zone)
                    e['rel_x'] = int(rel_x)
                    e['rel_y'] = int(rel_y)
                    found = e
                    break
            except Exception:
                continue
        if found is None:
            return None
        write_light_instances(data)
        _log.info(f"[LightInstances] Updated id={inst_id} -> zone={zone} rel=({rel_x},{rel_y})")
        return found
    except Exception:
        _log.exception("update_instance_position failed")
        return None


def delete_instances(ids: list[int] | set[int]) -> int:
    """Delete instances whose id is in ids. Returns number of deletions."""
    try:
        idset = {int(i) for i in ids}
    except Exception:
        idset = set()
    if not idset:
        return 0
    data = load_light_instances() or []
    before = len(data)
    kept: list[Dict[str, Any]] = []
    for e in data:
        try:
            if int(e.get('id')) in idset:
                continue
        except Exception:
            pass
        kept.append(e)
    if len(kept) != before:
        write_light_instances(kept)
    deleted = before - len(kept)
    if deleted:
        _log.info(f"[LightInstances] Deleted {deleted} instance(s): {sorted(idset)}")
    return deleted
