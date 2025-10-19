from __future__ import annotations

from typing import Any, Dict, List, Optional, Tuple
import logging

from .io_instances import load_instances_json

logger = logging.getLogger(__name__)

# Module-local cache: indexes by id and by (template_id, zone, tile)
_CACHE_DATA_REF: Optional[List[Dict[str, Any]]] = None
_INDEX_BY_ID: Dict[str, Tuple[int, Optional[Dict[str, Any]]]] = {}
_INDEX_BY_TPL_ZONE_TILE: Dict[Tuple[str, str, Tuple[int, int]], Tuple[int, Optional[Dict[str, Any]]]] = {}

def _rebuild_indexes_if_needed(data: List[Dict[str, Any]]) -> None:
    global _CACHE_DATA_REF, _INDEX_BY_ID, _INDEX_BY_TPL_ZONE_TILE
    if data is _CACHE_DATA_REF:
        return
    idx_by_id: Dict[str, Tuple[int, Optional[Dict[str, Any]]]] = {}
    idx_by_key: Dict[Tuple[str, str, Tuple[int, int]], Tuple[int, Optional[Dict[str, Any]]]] = {}
    for i, inst in enumerate(data):
        if not isinstance(inst, dict):
            continue
        try:
            # By id
            tid = str(inst.get('id'))
            if tid and tid not in idx_by_id:
                idx_by_id[tid] = (i, inst.get('overrides'))
            # By (template_id, zone, tile)
            tpl = str(inst.get('template_id'))
            zone = str(inst.get('zone'))
            tile = inst.get('tile', [0, 0])
            tx = int(tile[0]) if isinstance(tile, (list, tuple)) and len(tile) >= 2 else int(tile)
            ty = int(tile[1]) if isinstance(tile, (list, tuple)) and len(tile) >= 2 else 0
            key = (tpl, zone, (tx, ty))
            if key not in idx_by_key:
                idx_by_key[key] = (i, inst.get('overrides'))
        except Exception:
            continue
    _CACHE_DATA_REF = data
    _INDEX_BY_ID = idx_by_id
    _INDEX_BY_TPL_ZONE_TILE = idx_by_key


def find_instance_by_id(target_id: str) -> tuple[List[Dict[str, Any]], Optional[int], Optional[Dict[str, Any]]]:
    """Load JSON and find the instance by its 'id'. Returns (list, index, overrides)."""
    data = load_instances_json()
    _rebuild_indexes_if_needed(data)
    idx_found: Optional[int] = None
    overrides: Optional[Dict[str, Any]] = None
    try:
        hit = _INDEX_BY_ID.get(str(target_id))
        if hit is not None:
            idx_found, overrides = hit
    except Exception:
        pass
    # Debug; centralized RateLimitFilter will throttle repeated messages
    logger.debug(f"[spawner.persistence] find_instance_by_id('{target_id}') -> idx={idx_found}")

    return data, idx_found, overrides


def find_instance_in_json(template_id: str, zone: str, local_tile: Tuple[int, int]) -> tuple[List[Dict[str, Any]], Optional[int], Optional[Dict[str, Any]]]:
    """Load JSON and find the instance matching template_id, zone and tile=local_tile.
    Returns (instances_list, index or None, overrides or None).
    """
    data = load_instances_json()
    _rebuild_indexes_if_needed(data)
    idx_found: Optional[int] = None
    overrides: Optional[Dict[str, Any]] = None
    try:
        key = (str(template_id), str(zone), (int(local_tile[0]), int(local_tile[1])))
        hit = _INDEX_BY_TPL_ZONE_TILE.get(key)
        if hit is not None:
            idx_found, overrides = hit
    except Exception:
        pass
    # Debug; centralized RateLimitFilter will throttle repeated messages
    logger.debug(f"[spawner.persistence] find_instance_in_json(tpl={template_id}, zone={zone}, tile={local_tile}) -> idx={idx_found}")

    return data, idx_found, overrides
