from __future__ import annotations

from typing import Any, Dict, List
import json
import os
import logging

from . import paths as paths
from .ids import ensure_instance_ids

logger = logging.getLogger(__name__)


# Simple mtime-aware cache for instances JSON to avoid repeated disk reads
_INST_CACHE_PATH: str | None = None
_INST_CACHE_MTIME: float | None = None
_INST_CACHE_DATA: List[Dict[str, Any]] | None = None


def _safe_mtime(path: str) -> float | None:
    try:
        return os.path.getmtime(path)
    except OSError:
        return None


def load_instances_json() -> List[Dict[str, Any]]:
    path = paths.instances_path()
    try:
        # Fast path: serve from cache if mtime unchanged
        mtime = _safe_mtime(path)
        global _INST_CACHE_PATH, _INST_CACHE_MTIME, _INST_CACHE_DATA
        if (
            _INST_CACHE_PATH == path
            and _INST_CACHE_DATA is not None
            and _INST_CACHE_MTIME is not None
            and mtime is not None
            and _INST_CACHE_MTIME == mtime
        ):
            return _INST_CACHE_DATA

        with open(path, 'r', encoding='utf-8') as f:
            data = json.load(f)
        if not isinstance(data, list):
            _INST_CACHE_PATH, _INST_CACHE_MTIME, _INST_CACHE_DATA = path, mtime, []
            return []
        # Ensure every instance has a unique 'id' for robust identification
        changed, fixed = ensure_instance_ids(data)
        # Log; centralized RateLimitFilter will throttle repeated messages
        if changed:
            logger.debug(f"[spawner.persistence] load_instances_json: read {len(data)} entries from {path}; changed_ids=True")
        else:
            logger.debug(f"[spawner.persistence] load_instances_json: read {len(data)} entries from {path}")
        if changed:
            write_instances_json(fixed)
            # After write, refresh mtime and cache with the fixed data
            mtime2 = _safe_mtime(path)
            _INST_CACHE_PATH, _INST_CACHE_MTIME, _INST_CACHE_DATA = path, mtime2, fixed
            return fixed
        # Update cache with freshly read data
        _INST_CACHE_PATH, _INST_CACHE_MTIME, _INST_CACHE_DATA = path, mtime, data
        return data
    except FileNotFoundError:
        _INST_CACHE_PATH, _INST_CACHE_MTIME, _INST_CACHE_DATA = path, None, []
        return []
    except json.JSONDecodeError:
        logger.debug("load_instances_json: JSON decode error", exc_info=True)
        _INST_CACHE_PATH, _INST_CACHE_MTIME, _INST_CACHE_DATA = path, _safe_mtime(path), []
        return []
    except OSError:
        logger.debug("load_instances_json: OS error while reading file", exc_info=True)
        _INST_CACHE_PATH, _INST_CACHE_MTIME, _INST_CACHE_DATA = path, _safe_mtime(path), []
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
    # Update cache post-write to reflect latest contents
    try:
        global _INST_CACHE_PATH, _INST_CACHE_MTIME, _INST_CACHE_DATA
        _INST_CACHE_PATH = path
        _INST_CACHE_MTIME = _safe_mtime(path)
        _INST_CACHE_DATA = deduped
    except Exception:
        pass
