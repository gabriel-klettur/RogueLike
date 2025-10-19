from __future__ import annotations

from typing import Any, Dict, List, Optional, Tuple
import logging
import json

from roguelike_engine.config.map_config import global_map_settings

from .io_templates import load_spawners_json, write_spawners_json
from .io_instances import load_instances_json, write_instances_json
from .search import find_instance_by_id, find_instance_in_json
from .ids import generate_instance_id

logger = logging.getLogger(__name__)


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
