from __future__ import annotations

from typing import Any, Dict, List
import json
import os
import logging

from . import paths as paths
from .ids import ensure_instance_ids
from .logutil import dedup_should_log

logger = logging.getLogger(__name__)


def load_instances_json() -> List[Dict[str, Any]]:
    path = paths.instances_path()
    try:
        with open(path, 'r', encoding='utf-8') as f:
            data = json.load(f)
        if not isinstance(data, list):
            return []
        # Ensure every instance has a unique 'id' for robust identification
        changed, fixed = ensure_instance_ids(data)
        # Debug: de-duplicate noisy logs within a short window
        key = f"load_instances_json:{path}"
        allow, suppressed = dedup_should_log(key, window_ms=2000)
        if changed:
            logger.debug(f"[spawner.persistence] load_instances_json: read {len(data)} entries from {path}; changed_ids=True")
        elif allow:
            extra = f"; suppressed={suppressed}" if suppressed else ""
            logger.debug(f"[spawner.persistence] load_instances_json: read {len(data)} entries from {path}{extra}")
        if changed:
            write_instances_json(fixed)
            return fixed
        return data
    except FileNotFoundError:
        return []
    except json.JSONDecodeError:
        logger.debug("load_instances_json: JSON decode error", exc_info=True)
        return []
    except OSError:
        logger.debug("load_instances_json: OS error while reading file", exc_info=True)
        return []


def write_instances_json(data: List[Dict[str, Any]]) -> None:
    path = paths.instances_path()
    os.makedirs(os.path.dirname(path), exist_ok=True)
    # Sanitize legacy fields and normalize building_id inside overrides/root
    cleaned: List[Dict[str, Any]] = []
    for inst in data or []:
        if not isinstance(inst, dict):
            continue
        e = dict(inst)
        # Remove legacy keys at root for safety
        e.pop('spawner_img', None)
        e.pop('spawner_img_size', None)
        # Clean overrides
        ov = e.get('overrides')
        if isinstance(ov, dict):
            ov2 = dict(ov)
            ov2.pop('spawner_img', None)
            ov2.pop('spawner_img_size', None)
            # Normalize building_id
            if ov2.get('building_id') is not None:
                try:
                    ov2['building_id'] = int(ov2['building_id'])
                except (ValueError, TypeError):
                    pass
            e['overrides'] = ov2
        # Normalize root building_id
        if e.get('building_id') is not None:
            try:
                e['building_id'] = int(e['building_id'])
            except (ValueError, TypeError):
                pass
        cleaned.append(e)

    # Deduplicate by composite key (template_id, zone, tile) with last-wins policy
    unique_map: dict[tuple[str, str, tuple[int, int]], Dict[str, Any]] = {}
    for e in cleaned:
        try:
            tpl = str(e.get('template_id'))
            zone = str(e.get('zone'))
            tile = e.get('tile', [0, 0])
            tx = int(tile[0]) if isinstance(tile, (list, tuple)) and len(tile) >= 2 else int(tile)
            ty = int(tile[1]) if isinstance(tile, (list, tuple)) and len(tile) >= 2 else 0
            key = (tpl, zone, (tx, ty))
        except Exception:
            # If malformed, fall back to using object id to avoid crash; it won't dedup
            key = (str(e.get('template_id')), str(e.get('zone')), (id(e), 0))
        # Last occurrence overwrites previous to reflect most recent edit
        unique_map[key] = e

    deduped: List[Dict[str, Any]] = list(unique_map.values())
    # Ensure unique and valid string IDs post-dedup
    try:
        changed, fixed = ensure_instance_ids(deduped)
        deduped = fixed if changed else deduped
    except Exception:
        pass

    # Optional debug: report duplicates removed
    try:
        removed = max(0, len(cleaned) - len(deduped))
        if removed > 0:
            logger.debug(f"[spawner.persistence] write_instances_json: removed {removed} duplicate instance(s) by key (template_id, zone, tile)")
    except Exception:
        pass

    with open(path, 'w', encoding='utf-8') as f:
        json.dump(deduped, f, ensure_ascii=False, indent=2)
