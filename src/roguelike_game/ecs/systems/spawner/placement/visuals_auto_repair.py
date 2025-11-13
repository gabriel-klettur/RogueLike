from __future__ import annotations

import logging

from .buildings_repo import write_buildings_instances_json
from .visuals_building import append_building_object_in_world
from .spawner_visuals_persistence import persist_spawner_instance_visuals
from .visuals_common import (
    load_buildings_data,
    load_templates_map,
    get_zone,
    get_local_tile,
    extract_visual_fields,
    ensure_instance_scale_override,
    compute_new_entry,
    update_visuals_map_entry,
)
from .buildings_repo import get_template_image_path

logger = logging.getLogger(__name__)


def auto_repair_state_visuals(world, eid: int, cfg, inst: dict) -> None:
    vis = inst.get('visuals') if isinstance(inst, dict) else None
    if not isinstance(vis, dict) or not vis:
        return
    b_arr, existing_ids, max_id = load_buildings_data()
    templates, tmap = load_templates_map()
    zone = get_zone(inst)
    local_tile = get_local_tile(inst)

    if getattr(cfg, 'state_visuals', None) is None:
        try:
            cfg.state_visuals = {}
        except Exception:
            pass

    for key, val in list(vis.items()):
        cur_iid, tpl_id, visuals_scale = extract_visual_fields(val, cfg, str(key), capture_offsets=True)

        if cur_iid is not None and cur_iid in existing_ids:
            # Bind mapping to existing id
            try:
                cfg.state_visuals[str(key)] = int(cur_iid)
            except Exception:
                pass
            # Ensure any scale override is persisted
            ensure_instance_scale_override(b_arr, int(cur_iid), visuals_scale)
            # Ensure a Building object for this id exists in world memory; if not, reconstruct it
            try:
                missing = True
                for ob in getattr(world, 'buildings', []) or []:
                    try:
                        if getattr(ob, 'id', None) == int(cur_iid):
                            missing = False
                            break
                    except Exception:
                        continue
                if missing:
                    # Locate instance entry and its template, then append object to world
                    entry = None
                    for e in b_arr:
                        try:
                            if int(e.get('id')) == int(cur_iid):
                                entry = e
                                break
                        except Exception:
                            continue
                    if entry is not None:
                        try:
                            tpl_id2 = int(entry.get('template_id')) if entry.get('template_id') is not None else None
                        except Exception:
                            tpl_id2 = None
                        tpl_entry = tmap.get(tpl_id2) if tpl_id2 in tmap else None
                        try:
                            img_path = get_template_image_path(templates, int(tpl_id2)) if tpl_id2 is not None else None
                        except Exception:
                            img_path = None
                        append_building_object_in_world(world, entry, tpl_entry, img_path)
            except Exception:
                pass
            continue

        if tpl_id is None or tpl_id not in tmap:
            continue
        tpl_entry = tmap.get(tpl_id)
        entry, max_id = compute_new_entry(
            zone=zone,
            local_tile=local_tile,
            tpl_id=int(tpl_id),
            tpl_entry=tpl_entry,
            templates=templates,
            inst_id=str(inst.get('id')) if inst.get('id') is not None else None,
            visuals_scale=visuals_scale,
            include_spawner_visual_flag=False,
            max_id=max_id,
        )
        b_arr.append(entry)
        try:
            write_buildings_instances_json(b_arr)
            existing_ids.add(int(entry['id']))
        except Exception:
            logger.warning("[SpawnerPlacementSystem] Could not persist buildings_instances for auto-repair")
        try:
            img_path = get_template_image_path(templates, int(tpl_id))
        except Exception:
            img_path = None
        append_building_object_in_world(world, entry, tpl_entry, img_path)
        try:
            cfg.state_visuals[str(key)] = int(entry['id'])
        except Exception:
            pass
        try:
            update_visuals_map_entry(vis, str(key), val, int(entry['id']), int(tpl_id))
        except Exception:
            pass
        try:
            if not getattr(cfg, 'visible_in_game', False):
                cfg.visible_in_game = True
        except Exception:
            pass

    try:
        persist_spawner_instance_visuals(str(inst.get('id')) if inst.get('id') is not None else None, vis, ensure_visible_in_game=True)
    except Exception:
        pass
