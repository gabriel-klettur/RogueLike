from __future__ import annotations

from typing import Any, Dict, List, Optional, Tuple
import logging

from .io_instances import load_instances_json
from .logutil import dedup_should_log

logger = logging.getLogger(__name__)


def find_instance_by_id(target_id: str) -> tuple[List[Dict[str, Any]], Optional[int], Optional[Dict[str, Any]]]:
    """Load JSON and find the instance by its 'id'. Returns (list, index, overrides)."""
    data = load_instances_json()
    idx_found: Optional[int] = None
    overrides: Optional[Dict[str, Any]] = None
    for i, inst in enumerate(data):
        try:
            if str(inst.get('id')) == str(target_id):
                idx_found = i
                overrides = inst.get('overrides')
                break
        except (AttributeError, TypeError, ValueError):
            continue
    # Debug: de-duplicate lookups by id
    key = f"find_by_id:{target_id}"
    allow, suppressed = dedup_should_log(key, window_ms=2000)
    if allow:
        extra = f"; suppressed={suppressed}" if suppressed else ""
        logger.debug(f"[spawner.persistence] find_instance_by_id('{target_id}') -> idx={idx_found}{extra}")

    return data, idx_found, overrides


def find_instance_in_json(template_id: str, zone: str, local_tile: Tuple[int, int]) -> tuple[List[Dict[str, Any]], Optional[int], Optional[Dict[str, Any]]]:
    """Load JSON and find the instance matching template_id, zone and tile=local_tile.
    Returns (instances_list, index or None, overrides or None).
    """
    data = load_instances_json()
    idx_found: Optional[int] = None
    overrides: Optional[Dict[str, Any]] = None
    for i, inst in enumerate(data):
        try:
            if inst.get('template_id') == template_id and inst.get('zone') == zone:
                tile = inst.get('tile', [0, 0])
                if tuple(tile) == tuple(local_tile):
                    idx_found = i
                    overrides = inst.get('overrides')
                    break
        except (AttributeError, TypeError, ValueError):
            continue
    key = f"find_in_json:{template_id}:{zone}:{local_tile}"
    allow, suppressed = dedup_should_log(key, window_ms=2000)
    if allow:
        extra = f"; suppressed={suppressed}" if suppressed else ""
        logger.debug(f"[spawner.persistence] find_instance_in_json(tpl={template_id}, zone={zone}, tile={local_tile}) -> idx={idx_found}{extra}")

    return data, idx_found, overrides
