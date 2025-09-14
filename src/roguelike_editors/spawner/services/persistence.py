from __future__ import annotations

from typing import Optional, Tuple, List, Dict, Any
import os
import json
import re
import uuid

from roguelike_engine.config import config
import time as _time
from roguelike_engine.config.map_config import global_map_settings
import logging

# Module logger (respect global configuration)
logger = logging.getLogger(__name__)

# Lightweight throttling state for noisy debug logs (see _DEDUP_TIMERS below)

# Generic de-duplication for noisy logs (keyed windows)
_DEDUP_TIMERS: Dict[str, Tuple[int, int]] = {}  # key -> (last_ms, suppressed_count)

def _now_ms() -> int:
    return int(_time.monotonic() * 1000)

def _dedup_should_log(key: str, window_ms: int = 2000) -> Tuple[bool, int]:
    """Return (allow, suppressed_count).

    If called repeatedly within window_ms for the same key, we suppress logs and
    accumulate a counter. On the first call after the window elapses, we allow the
    log and return how many duplicates were suppressed in that period.
    """
    now = _now_ms()
    last, count = _DEDUP_TIMERS.get(key, (-10_000_000, 0))
    if now - last >= window_ms:
        # allow log; return suppressed count and reset counter
        _DEDUP_TIMERS[key] = (now, 0)
        return True, count
    else:
        # suppress and accumulate
        _DEDUP_TIMERS[key] = (last, count + 1)
        return False, 0


def instances_path() -> str:
    base = getattr(config, 'DATA_DIR', 'data')
    return os.path.join(base, 'spawners', 'spawners_instances.json')


def spawners_path() -> str:
    base = getattr(config, 'DATA_DIR', 'data')
    return os.path.join(base, 'spawners', 'spawners_templates.json')


def load_instances_json() -> List[Dict[str, Any]]:
    path = instances_path()
    try:
        with open(path, 'r', encoding='utf-8') as f:
            data = json.load(f)
        if not isinstance(data, list):
            return []
        # Ensure every instance has a unique 'id' for robust identification
        changed, fixed = ensure_instance_ids(data)
        # Debug: de-duplicate noisy logs within a short window
        key = f"load_instances_json:{path}"
        allow, suppressed = _dedup_should_log(key, window_ms=2000)
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


def zone_for_global_tile(tx: int, ty: int) -> Optional[str]:
    """Return the zone name that contains the global tile (tx, ty), or None.

    Uses `global_map_settings.zone_offsets` and zone_size.
    """
    try:
        w, h = global_map_settings.zone_size
        for name, (ox, oy) in global_map_settings.zone_offsets.items():
            # Skip sentinel entries
            if name in ('no zone', 'no-zone'):
                continue
            if ox <= tx < ox + w and oy <= ty < oy + h:
                return name
    except (AttributeError, KeyError, TypeError, ValueError):
        logger.debug("zone_for_global_tile: failed while computing zone", exc_info=True)
    return None


def load_spawners_json() -> List[Dict[str, Any]]:
    path = spawners_path()
    try:
        with open(path, 'r', encoding='utf-8') as f:
            data = json.load(f)
        if not isinstance(data, list):
            return []
        # Sanitize legacy fields
        for sp in data:
            try:
                if isinstance(sp, dict):
                    sp.pop('spawner_img', None)
                    sp.pop('spawner_img_size', None)
                    # Normalize building_id to int if possible
                    if sp.get('building_id') is not None:
                        try:
                            sp['building_id'] = int(sp['building_id'])
                        except (ValueError, TypeError):
                            pass
            except (AttributeError, KeyError, TypeError):
                continue
        return data
    except FileNotFoundError:
        return []
    except json.JSONDecodeError:
        logger.debug("load_spawners_json: JSON decode error", exc_info=True)
        return []
    except OSError:
        logger.debug("load_spawners_json: OS error while reading file", exc_info=True)
        return []


def write_spawners_json(data: List[Dict[str, Any]]) -> None:
    """Write the full spawners list to data/spawners/spawners_templates.json."""
    path = spawners_path()
    os.makedirs(os.path.dirname(path), exist_ok=True)
    # Sanitize legacy fields before persisting
    cleaned: List[Dict[str, Any]] = []
    for sp in data or []:
        if not isinstance(sp, dict):
            continue
        sp2 = dict(sp)
        sp2.pop('spawner_img', None)
        sp2.pop('spawner_img_size', None)
        # Normalize building_id
        if sp2.get('building_id') is not None:
            try:
                sp2['building_id'] = int(sp2['building_id'])
            except (ValueError, TypeError):
                pass
        cleaned.append(sp2)
    with open(path, 'w', encoding='utf-8') as f:
        json.dump(cleaned, f, ensure_ascii=False, indent=2)
    logger.debug(f"[spawner.persistence] write_spawners_json: wrote {len(cleaned)} templates to {path}")


def save_spawner_template(updated: Dict[str, Any]) -> None:
    """Update or append a single spawner template in spawners_templates.json by id.

    If an entry with the same 'id' exists, replace it in-place; otherwise append it.
    """
    sid = str(updated.get('id')) if isinstance(updated, dict) else None  # type: ignore
    data = load_spawners_json()
    replaced = False
    if sid:
        for i, sp in enumerate(data):
            try:
                if str(sp.get('id')) == sid:
                    data[i] = updated
                    replaced = True
                    break
            except (AttributeError, TypeError, ValueError):
                continue
    if not replaced:
        data.append(updated)
    write_spawners_json(data)


def rename_spawner_template_id(old_id: str, new_id: str) -> Optional[Dict[str, Any]]:
    """Safely rename a spawner template id across spawners_templates.json and spawners_instances.json.

    - If new_id already exists and is different from old_id -> do nothing (return None).
    - Otherwise, update the template entry id and update all instance entries' template_id.
    - Returns the updated template dict on success, else None.
    """
    if not old_id or not new_id or str(old_id) == str(new_id):
        return None
    data = load_spawners_json()
    # Check conflict
    for sp in data:
        try:
            sid = str(sp.get('id'))
        except (AttributeError, TypeError, ValueError):
            continue
        if sid == str(new_id) and sid != str(old_id):
            return None  # conflict
    # Find template by old_id
    idx = None
    for i, sp in enumerate(data):
        try:
            if str(sp.get('id')) == str(old_id):
                idx = i
                break
        except (AttributeError, TypeError, ValueError):
            continue
    if idx is None:
        return None
    # Update id and persist templates
    data[idx]['id'] = str(new_id)
    write_spawners_json(data)
    updated_tpl = data[idx]
    # Update instances
    try:
        inst_list = load_instances_json()
        changed = False
        for inst in inst_list:
            try:
                if str(inst.get('template_id')) == str(old_id):
                    inst['template_id'] = str(new_id)
                    changed = True
            except (AttributeError, TypeError, ValueError):
                continue
        if changed:
            write_instances_json(inst_list)
    except (OSError, json.JSONDecodeError):
        logger.debug("rename_spawner_template_id: failed to update instances with new template_id", exc_info=True)
    return updated_tpl


def write_instances_json(data: List[Dict[str, Any]]) -> None:
    path = instances_path()
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
    with open(path, 'w', encoding='utf-8') as f:
        json.dump(cleaned, f, ensure_ascii=False, indent=2)


def slugify(s: str) -> str:
    s = str(s)
    s = s.strip().lower()
    s = re.sub(r"[^a-z0-9]+", "_", s)
    s = re.sub(r"_+", "_", s)
    return s.strip('_')


def generate_instance_id(inst: Dict[str, Any], existing_ids: set[str]) -> str:
    tpl = slugify(inst.get('template_id', 'tpl'))
    zone = slugify(inst.get('zone', 'zone'))
    try:
        tile = inst.get('tile', [0, 0])
        x, y = int(tile[0]), int(tile[1])
    except (KeyError, TypeError, ValueError, AttributeError):
        x, y = 0, 0
    base = f"{tpl}_{zone}_{x}_{y}" if tpl or zone else f"inst_{x}_{y}"
    if not base:
        base = f"inst_{uuid.uuid4().hex[:8]}"
    candidate = base
    i = 1
    while candidate in existing_ids:
        i += 1
        candidate = f"{base}_{i}"
    return candidate


def ensure_instance_ids(data: List[Dict[str, Any]]) -> tuple[bool, List[Dict[str, Any]]]:
    """Ensure each instance dict has a unique 'id' (string). Returns (changed, data)."""
    changed = False
    ids: set[str] = set()
    # First pass: normalize and collect
    for inst in data:
        cur = inst.get('id')
        if cur is not None:
            try:
                s = str(cur).strip()
            except Exception:
                s = ""
            if s:
                # ensure uniqueness
                if s in ids:
                    # will regenerate in second pass
                    inst['id'] = None  # type: ignore
                    changed = True
                else:
                    inst['id'] = s
                    ids.add(s)
            else:
                inst.pop('id', None)
                changed = True
    # Second pass: generate for missing or duplicated
    for inst in data:
        if not inst.get('id'):
            new_id = generate_instance_id(inst, ids)
            inst['id'] = new_id
            ids.add(new_id)
            changed = True
    return changed, data


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
    allow, suppressed = _dedup_should_log(key, window_ms=2000)
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
    allow, suppressed = _dedup_should_log(key, window_ms=2000)
    if allow:
        extra = f"; suppressed={suppressed}" if suppressed else ""
        logger.debug(f"[spawner.persistence] find_instance_in_json(tpl={template_id}, zone={zone}, tile={local_tile}) -> idx={idx_found}{extra}")

    return data, idx_found, overrides


def persist_drop(world,
                 eid: int,
                 drag_start_entry: Optional[Dict[str, Any]],
                 *,
                 override_zone: Optional[str] = None,
                 orig_zone: Optional[str] = None,
                 overrides_update: Optional[Dict[str, Any]] = None) -> None:
    """Persist a moved spawner's anchor tile back to spawners_instances.json.

    - Computes local tile with zone offset (using override_zone if provided, else cfg.zone)
    - Looks up original entry in orig_zone (if provided) or snapshot zone to replace in-place
    - Preserves overrides if present in snapshot or existing entry
    - Replaces existing entry or appends a new one
    """
    comps = getattr(world, 'components', {})
    if 'SpawnerConfig' not in comps or eid not in comps['SpawnerConfig']:
        return
    cfg = comps['SpawnerConfig'][eid]
    # Target zone to persist under
    zone = override_zone or cfg.zone
    off_x, off_y = global_map_settings.zone_offsets.get(zone, (0, 0))
    tx, ty = cfg.anchor_tile
    new_local = (int(tx - off_x), int(ty - off_y))
    tpl_id = cfg.template_id
    # Optional stable identifier captured from snapshot
    inst_id = None
    try:
        if drag_start_entry and drag_start_entry.get('id'):
            inst_id = str(drag_start_entry.get('id'))
    except (AttributeError, KeyError, TypeError, ValueError):
        logger.debug("persist_drop: failed to read snapshot id", exc_info=True)
        inst_id = None

    # Try to find by original local tile first (if we captured it)
    orig_local = None
    if drag_start_entry and drag_start_entry.get('local_tile') is not None:
        try:
            orig_local = tuple(drag_start_entry['local_tile'])
        except (KeyError, TypeError):
            orig_local = None

    # Where to search the existing entry
    lookup_zone = orig_zone or (drag_start_entry.get('zone') if drag_start_entry else None) or zone
    # 1) Prefer lookup by stable instance id if available
    if inst_id:
        data, idx_found, _ = find_instance_by_id(inst_id)
    else:
        data, idx_found, _ = find_instance_in_json(tpl_id, lookup_zone, orig_local or new_local)
        if idx_found is None:
            # Try by new location in case snapshot is missing
            _, idx_found, _ = find_instance_in_json(tpl_id, zone, new_local)

    entry: Dict[str, Any] = {
        'template_id': tpl_id,
        'zone': zone,
        'tile': [int(new_local[0]), int(new_local[1])],
    }
    # Preserve overrides (snapshot has priority) and merge with overrides_update if provided
    overrides: Dict[str, Any] = {}
    if drag_start_entry and drag_start_entry.get('overrides') is not None:
        try:
            src = drag_start_entry['overrides']
            if isinstance(src, dict):
                overrides.update(src)
        except (KeyError, TypeError, AttributeError):
            logger.debug("persist_drop: failed to merge snapshot overrides", exc_info=True)
    elif idx_found is not None:
        try:
            src = data[idx_found].get('overrides')
            if isinstance(src, dict):
                overrides.update(src)
        except (IndexError, AttributeError, TypeError):
            logger.debug("persist_drop: failed to merge existing overrides", exc_info=True)
    # Apply incoming updates (e.g., building_id or other overrides edited in the editor)
    if isinstance(overrides_update, dict):
        overrides.update(overrides_update)
    # Sanitize overrides: drop legacy fields and normalize building_id
    try:
        overrides.pop('spawner_img', None)
        overrides.pop('spawner_img_size', None)
        if overrides.get('building_id') is not None:
            try:
                overrides['building_id'] = int(overrides['building_id'])
            except (ValueError, TypeError):
                logger.debug("persist_drop: failed to normalize overrides.building_id", exc_info=True)
    except (AttributeError, KeyError, TypeError):
        logger.debug("persist_drop: failed to sanitize overrides", exc_info=True)
    if overrides:
        entry['overrides'] = overrides

    # Preserve visuals from previous entry if present (avoid losing visuals on move)
    try:
        prev_visuals = None
        if idx_found is not None:
            prev_visuals = data[idx_found].get('visuals')
        elif inst_id:
            # try lookup by id to preserve visuals
            for e in data:
                try:
                    if str(e.get('id')) == str(inst_id):
                        prev_visuals = e.get('visuals')
                        break
                except (AttributeError, TypeError, ValueError):
                    continue
        if isinstance(prev_visuals, dict) and prev_visuals:
            entry['visuals'] = prev_visuals
        logger.debug(f"[spawner.persistence] persist_drop: tpl={tpl_id} zone={zone} new_local={new_local} idx_found={idx_found} preserved_visuals_len={len(prev_visuals or {})}")
    except (AttributeError, TypeError, ValueError, IndexError):
        logger.debug("persist_drop: failed while preserving visuals", exc_info=True)

    # Preserve or assign instance id
    try:
        if idx_found is not None:
            prev_id = data[idx_found].get('id')
            if prev_id:
                entry['id'] = prev_id
            elif inst_id:
                entry['id'] = inst_id
        else:
            # If snapshot had an id, reuse it as long as it's unique; otherwise generate
            existing_ids = {str(x.get('id')) for x in data if x.get('id')}
            if inst_id and inst_id not in existing_ids:
                entry['id'] = inst_id
            else:
                entry['id'] = generate_instance_id(entry, existing_ids)
    except (AttributeError, IndexError, TypeError, ValueError):
        pass

    if idx_found is not None:
        data[idx_found] = entry
    else:
        data.append(entry)

    write_instances_json(data)
    logger.info(f"[spawner.persistence] persist_drop: wrote entry id={entry.get('id')} visuals_len={len((entry.get('visuals') or {}))}")


def remove_visual_refs_by_building_id(bid: int) -> int:
    """Remove any visuals entries across all spawner instances that reference a given
    building instance id. Returns the number of visuals entries removed.

    A visuals mapping value can be either an int (legacy) or a dict containing
    'instance_id'/'id'/'building_instance_id'.
    """
    try:
        bid = int(bid)
    except (ValueError, TypeError):
        return 0
    data = load_instances_json()
    removed = 0
    changed = False
    for inst in data:
        try:
            vis = inst.get('visuals')
            if not isinstance(vis, dict) or not vis:
                continue
            keys = list(vis.keys())
            for k in keys:
                v = vis.get(k)
                try:
                    if isinstance(v, dict):
                        vid = v.get('instance_id') or v.get('id') or v.get('building_instance_id')
                        vid = int(vid) if vid is not None else None
                    else:
                        vid = int(v)
                except (ValueError, TypeError):
                    vid = None
                if vid is not None and int(vid) == int(bid):
                    vis.pop(k, None)
                    removed += 1
                    changed = True
            if changed:
                inst['visuals'] = vis
        except Exception:
            continue
    if changed:
        write_instances_json(data)
        logger.info(f"[spawner.persistence] remove_visual_refs_by_building_id({bid}) -> removed={removed}")
    return removed
