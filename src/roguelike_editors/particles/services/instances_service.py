from __future__ import annotations

from typing import List, Dict, Any
import json
import os
from roguelike_engine.config.config import PARTICLES_INSTANCES_PATH
from roguelike_editors.buildings.utils.zone_helpers import detect_zone_from_px
from roguelike_engine.config.config_tiles import TILE_SIZE
import logging
_log = logging.getLogger(__name__)


def load_particles_instances() -> List[Dict[str, Any]]:
    """Read particles instances JSON. Always returns a list (possibly empty)."""
    path = PARTICLES_INSTANCES_PATH
    try:
        with open(path, 'r', encoding='utf-8') as f:
            data = json.load(f)
        return data if isinstance(data, list) else []
    except FileNotFoundError:
        return []
    except Exception:
        return []


essential_keys = ('id', 'preset_id', 'zone', 'rel_x', 'rel_y')


def write_particles_instances(data: List[Dict[str, Any]]) -> None:
    """Write particles instances JSON with indent and UTF-8. Creates parent dir.
    Performs basic deduplication by (zone, rel_x, rel_y, preset_id). Stable sort by id.
    """
    path = PARTICLES_INSTANCES_PATH
    os.makedirs(os.path.dirname(path), exist_ok=True)
    try:
        # Snapshot for audit
        _old = load_particles_instances()
    except Exception:
        _old = []
    # Dedup by key
    try:
        def _key(e: Dict[str, Any]) -> str:
            try:
                zone = str(e.get('zone') or 'no zone')
                rx = int(e.get('rel_x') or 0)
                ry = int(e.get('rel_y') or 0)
                pid = str(e.get('preset_id') or '')
                return f"{zone}|{rx}|{ry}|{pid}"
            except Exception:
                return repr(e)
        seen: Dict[str, Dict[str, Any]] = {}
        for e in list(data or []):
            k = _key(e)
            if k not in seen:
                seen[k] = e
        data = list(seen.values())
        # Stable order by id if present
        try:
            data.sort(key=lambda x: int(x.get('id') or 0))
        except Exception:
            pass
    except Exception:
        data = list(data or [])
    # Audit new vs old ids
    try:
        old_ids = {int(e.get('id')) for e in (_old or []) if isinstance(e, dict) and e.get('id') is not None}
        new_ids = {int(e.get('id')) for e in (data or []) if isinstance(e, dict) and e.get('id') is not None}
        added = sorted(new_ids - old_ids)
        removed = sorted(old_ids - new_ids)
        if added:
            _log.debug(f"[ParticlesInstances] Added IDs: {added}")
        if removed:
            _log.debug(f"[ParticlesInstances] Removed IDs: {removed}")
    except Exception:
        pass
    with open(path, 'w', encoding='utf-8') as f:
        json.dump(data or [], f, ensure_ascii=False, indent=2)


def append_instance(preset_id: str, world_x: float, world_y: float) -> Dict[str, Any]:
    """Append a new particle instance to JSON and return the entry created.

    Computes zone and rel_x/rel_y from world pixel coordinates using zone offsets.
    Assigns a monotonic integer id (max+1) to the new entry.
    """
    data = load_particles_instances() or []
    # Compute zone and relative pixel coordinates
    zone, (off_tx, off_ty) = detect_zone_from_px(world_x, world_y)
    origin_px_x = int(off_tx) * TILE_SIZE
    origin_px_y = int(off_ty) * TILE_SIZE
    rel_x = int(world_x - origin_px_x)
    rel_y = int(world_y - origin_px_y)
    # Next id
    try:
        next_id = 1 + max((int(e.get('id')) for e in data if isinstance(e, dict) and e.get('id') is not None), default=0)
    except Exception:
        next_id = len(data) + 1
    entry = {
        'id': int(next_id),
        'preset_id': str(preset_id),
        'zone': str(zone),
        'rel_x': int(rel_x),
        'rel_y': int(rel_y),
    }
    data.append(entry)
    write_particles_instances(data)
    _log.info(f"[ParticlesInstances] Added id={next_id} preset={preset_id} zone={zone} rel=({rel_x},{rel_y})")
    return entry


def remove_nearest_instance(world_x: float, world_y: float, max_dist_px: int = 48) -> Dict[str, Any] | None:
    """Remove the nearest persisted particle instance to the given world pos.

    Returns the removed entry dict or None if nothing within max_dist_px.
    """
    data = load_particles_instances() or []
    if not data:
        return None
    # Find nearest by world distance
    best_idx = -1
    best_d2 = None
    for i, e in enumerate(data):
        try:
            zone = str(e.get('zone') or 'no zone')
            rel_x = int(e.get('rel_x') or 0)
            rel_y = int(e.get('rel_y') or 0)
        except Exception:
            continue
        # Convert to world px
        try:
            from roguelike_engine.config.map_config import global_map_settings
            off_tx, off_ty = global_map_settings.zone_offsets.get(zone, (0, 0))
        except Exception:
            off_tx, off_ty = (0, 0)
        wx = int(off_tx) * TILE_SIZE + int(rel_x)
        wy = int(off_ty) * TILE_SIZE + int(rel_y)
        dx = float(world_x) - float(wx)
        dy = float(world_y) - float(wy)
        d2 = dx*dx + dy*dy
        if best_d2 is None or d2 < best_d2:
            best_d2 = d2
            best_idx = i
    if best_idx < 0:
        return None
    # Check threshold
    if best_d2 is None or best_d2 > float(max_dist_px * max_dist_px):
        return None
    removed = data.pop(best_idx)
    write_particles_instances(data)
    try:
        rid = removed.get('id')
    except Exception:
        rid = None
    _log.info(f"[ParticlesInstances] Removed id={rid} at world≈({world_x:.1f},{world_y:.1f}) d≈{(best_d2**0.5):.1f}px")
    return removed
