from __future__ import annotations

from typing import Dict, Optional, Tuple, Any
import logging

from .buildings_repo import (
    load_buildings_instances_json,
    write_buildings_instances_json,
    load_buildings_templates_json,
    get_template_image_path,
)
from .visuals_geometry import calc_centered_rel

logger = logging.getLogger(__name__)


def load_buildings_data():
    """Load buildings instances array and derive existing ids set and max id."""
    b_arr = load_buildings_instances_json()
    existing_ids = set()
    max_id = 0
    for e in b_arr:
        try:
            bid = int(e.get('id'))
            existing_ids.add(bid)
            if bid > max_id:
                max_id = bid
        except Exception:
            continue
    return b_arr, existing_ids, max_id


essential_template_keys = ("id",)


def load_templates_map() -> tuple[list[dict], Dict[int, dict]]:
    """Load templates list and build id->template mapping."""
    templates = load_buildings_templates_json()
    tmap: Dict[int, dict] = {}
    for t in templates:
        try:
            tmap[int(t.get('id'))] = t
        except Exception:
            continue
    return templates, tmap


def get_zone(inst: dict) -> str:
    try:
        return str(inst.get('zone')) if inst.get('zone') is not None else 'lobby'
    except Exception:
        return 'lobby'


def get_local_tile(inst: dict) -> Tuple[int, int]:
    try:
        local_tile = inst.get('tile') or (0, 0)
        return (int(local_tile[0]), int(local_tile[1]))
    except Exception:
        return (0, 0)


def extract_visual_fields(val: Any, cfg: Any | None, key: str | None, capture_offsets: bool) -> tuple[Optional[int], Optional[int], Optional[Tuple[int, int]]]:
    """Extract instance id, template id, and scale from a visuals[*] value.

    Optionally capture per-key pixel offsets into cfg.visuals_offsets_px.
    """
    cur_iid: Optional[int] = None
    tpl_id: Optional[int] = None
    visuals_scale: Optional[Tuple[int, int]] = None
    if isinstance(val, dict):
        try:
            cur_iid = int(val.get('instance_id') or val.get('id') or val.get('building_instance_id'))
        except Exception:
            cur_iid = None
        try:
            tpl_id = int(val.get('template_id')) if val.get('template_id') is not None else None
        except Exception:
            tpl_id = None
        try:
            if capture_offsets and cfg is not None and key is not None:
                off = val.get('offset')
                if isinstance(off, (list, tuple)) and len(off) == 2:
                    dx, dy = int(off[0]), int(off[1])
                    try:
                        if getattr(cfg, 'visuals_offsets_px', None) is None:
                            cfg.visuals_offsets_px = {}
                    except Exception:
                        pass
                    try:
                        cfg.visuals_offsets_px[str(key).strip().lower()] = (dx, dy)
                    except Exception:
                        pass
            sc = val.get('scale')
            if isinstance(sc, (list, tuple)) and len(sc) == 2:
                sw, sh = int(sc[0]), int(sc[1])
                if sw > 0 and sh > 0:
                    visuals_scale = (sw, sh)
        except Exception:
            visuals_scale = visuals_scale
    else:
        try:
            cur_iid = int(val)
        except Exception:
            cur_iid = None
    return cur_iid, tpl_id, visuals_scale


def ensure_instance_scale_override(
    b_arr: list[dict],
    instance_id: int,
    visuals_scale: Optional[Tuple[int, int]],
) -> bool:
    """Ensure b_arr[instance_id].overrides.scale equals visuals_scale.

    Returns True if b_arr changed and was persisted.
    """
    if visuals_scale is None:
        return False
    changed_bi = False
    try:
        for e in b_arr:
            try:
                if int(e.get('id')) != int(instance_id):
                    continue
            except Exception:
                continue
            ov = e.get('overrides') or {}
            if not isinstance(ov, dict):
                ov = {}
            try:
                cur_sc = ov.get('scale')
                cur_sc_t = (int(cur_sc[0]), int(cur_sc[1])) if isinstance(cur_sc, (list, tuple)) and len(cur_sc) == 2 else None
            except Exception:
                cur_sc_t = None
            if cur_sc_t != visuals_scale:
                ov['scale'] = [int(visuals_scale[0]), int(visuals_scale[1])]
                e['overrides'] = ov
                changed_bi = True
            break
        if changed_bi:
            try:
                write_buildings_instances_json(b_arr)
                return True
            except Exception:
                logger.warning("[SpawnerPlacementSystem] Could not persist scale override for existing building instance")
                return False
    except Exception:
        return False


def _compute_rel_key(
    zone: str,
    local_tile: Tuple[int, int],
    tpl_id: int,
    tpl_entry: dict | None,
    templates: list[dict],
) -> tuple[str, int, int, int]:
    """Compute the composite key (zone, rel_x, rel_y, template_id) used to detect duplicates.

    This mirrors the positioning logic used when creating new entries, ensuring we search
    for an already-existing building with the exact same computed rel coordinates.
    """
    img_path = get_template_image_path(templates, tpl_id)
    rel_x, rel_y, _scale = calc_centered_rel(local_tile, tpl_entry, img_path)
    try:
        z = str(zone)
    except Exception:
        z = str(zone)
    return (z, int(rel_x), int(rel_y), int(tpl_id))


def find_existing_instance_id_by_key(
    b_arr: list[dict],
    zone: str,
    local_tile: Tuple[int, int],
    tpl_id: int,
    tpl_entry: dict | None,
    templates: list[dict],
) -> Optional[int]:
    """Search in b_arr for an existing instance with same (zone, rel_x, rel_y, template_id).

    Returns the instance id if found, otherwise None.
    """
    try:
        z, rx, ry, tid = _compute_rel_key(zone, local_tile, tpl_id, tpl_entry, templates)
    except Exception:
        return None
    for e in b_arr:
        try:
            ez = str(e.get('zone'))
            erx = int(e.get('rel_x'))
            ery = int(e.get('rel_y'))
            etid = int(e.get('template_id'))
            if ez == z and erx == rx and ery == ry and etid == tid:
                try:
                    return int(e.get('id'))
                except Exception:
                    continue
        except Exception:
            continue
    return None


def compute_new_entry(
    zone: str,
    local_tile: Tuple[int, int],
    tpl_id: int,
    tpl_entry: dict | None,
    templates: list[dict],
    inst_id: str | None,
    visuals_scale: Optional[Tuple[int, int]],
    include_spawner_visual_flag: bool,
    max_id: int,
) -> tuple[dict, int]:
    """Build a new building instance entry with centered rel and proper overrides.

    Returns (entry, new_max_id).
    """
    img_path = get_template_image_path(templates, tpl_id)
    rel_x, rel_y, scale = calc_centered_rel(local_tile, tpl_entry, img_path)
    new_id = max_id + 1
    entry = {
        'id': int(new_id),
        'template_id': int(tpl_id),
        'zone': zone,
        'rel_x': int(rel_x),
        'rel_y': int(rel_y),
        'overrides': {
            '_is_spawner_visual': True,
        },
        'spawn_id': inst_id,
        'spawner_instance_id': inst_id,
    }
    if include_spawner_visual_flag:
        entry['spawner_visual'] = True
    if visuals_scale is not None:
        try:
            entry['overrides']['scale'] = [int(visuals_scale[0]), int(visuals_scale[1])]  # type: ignore[index]
        except Exception:
            pass
    elif scale is not None:
        try:
            entry['overrides']['scale'] = [int(scale[0]), int(scale[1])]  # type: ignore[index]
        except Exception:
            pass
    return entry, new_id


def update_visuals_map_entry(vis: dict, key: str, val: Any, new_id: int, tpl_id: int) -> None:
    """Mutate the spawner 'visuals' mapping for key with new instance and preserve offset if present."""
    preserved_offset = None
    try:
        if isinstance(val, dict) and isinstance(val.get('offset'), (list, tuple)) and len(val.get('offset')) == 2:
            preserved_offset = [int(val['offset'][0]), int(val['offset'][1])]
    except Exception:
        preserved_offset = None
    entry_map = dict(val) if isinstance(val, dict) else {}
    entry_map['instance_id'] = int(new_id)
    entry_map['template_id'] = int(tpl_id)
    if preserved_offset is not None:
        entry_map['offset'] = preserved_offset  # type: ignore[index]
    vis[str(key)] = entry_map


 
