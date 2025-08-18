from __future__ import annotations

from typing import Optional, Tuple, List, Dict, Any
import os
import json

from roguelike_engine.config import config
from roguelike_engine.config.map_config import global_map_settings


def instances_path() -> str:
    base = getattr(config, 'DATA_DIR', 'data')
    return os.path.join(base, 'spawners', 'instances.json')


def spawners_path() -> str:
    base = getattr(config, 'DATA_DIR', 'data')
    return os.path.join(base, 'spawners', 'spawners.json')


def load_instances_json() -> List[Dict[str, Any]]:
    path = instances_path()
    try:
        with open(path, 'r', encoding='utf-8') as f:
            data = json.load(f)
        return data if isinstance(data, list) else []
    except FileNotFoundError:
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
    except Exception:
        pass
    return None


def load_spawners_json() -> List[Dict[str, Any]]:
    path = spawners_path()
    try:
        with open(path, 'r', encoding='utf-8') as f:
            data = json.load(f)
        return data if isinstance(data, list) else []
    except FileNotFoundError:
        return []


def write_spawners_json(data: List[Dict[str, Any]]) -> None:
    """Write the full spawners list to data/spawners/spawners.json."""
    path = spawners_path()
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, 'w', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False, indent=2)


def save_spawner_template(updated: Dict[str, Any]) -> None:
    """Update or append a single spawner template in spawners.json by id.

    If an entry with the same 'id' exists, replace it in-place; otherwise append it.
    """
    try:
        sid = str(updated.get('id'))
    except Exception:
        sid = None  # type: ignore
    data = load_spawners_json()
    replaced = False
    if sid:
        for i, sp in enumerate(data):
            try:
                if str(sp.get('id')) == sid:
                    data[i] = updated
                    replaced = True
                    break
            except Exception:
                continue
    if not replaced:
        data.append(updated)
    write_spawners_json(data)


def rename_spawner_template_id(old_id: str, new_id: str) -> Optional[Dict[str, Any]]:
    """Safely rename a spawner template id across spawners.json and instances.json.

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
        except Exception:
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
        except Exception:
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
            except Exception:
                continue
        if changed:
            write_instances_json(inst_list)
    except Exception:
        pass
    return updated_tpl


def write_instances_json(data: List[Dict[str, Any]]) -> None:
    path = instances_path()
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, 'w', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False, indent=2)


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
        except Exception:
            continue
    return data, idx_found, overrides


def persist_drop(world,
                 eid: int,
                 drag_start_entry: Optional[Dict[str, Any]],
                 *,
                 override_zone: Optional[str] = None,
                 orig_zone: Optional[str] = None) -> None:
    """Persist a moved spawner's anchor tile back to instances.json.

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

    # Try to find by original local tile first (if we captured it)
    orig_local = None
    if drag_start_entry and drag_start_entry.get('local_tile') is not None:
        orig_local = tuple(drag_start_entry['local_tile'])

    # Where to search the existing entry
    lookup_zone = orig_zone or (drag_start_entry.get('zone') if drag_start_entry else None) or zone
    data, idx_found, _ = find_instance_in_json(tpl_id, lookup_zone, orig_local or new_local)
    if idx_found is None:
        # Try by new location in case snapshot is missing
        _, idx_found, _ = find_instance_in_json(tpl_id, zone, new_local)

    entry: Dict[str, Any] = {
        'template_id': tpl_id,
        'zone': zone,
        'tile': [int(new_local[0]), int(new_local[1])],
    }
    # Preserve overrides (snapshot has priority)
    overrides = None
    if drag_start_entry and drag_start_entry.get('overrides') is not None:
        overrides = drag_start_entry['overrides']
    elif idx_found is not None:
        try:
            overrides = data[idx_found].get('overrides')
        except Exception:
            overrides = None
    if overrides is not None:
        entry['overrides'] = overrides

    if idx_found is not None:
        data[idx_found] = entry
    else:
        data.append(entry)

    write_instances_json(data)
