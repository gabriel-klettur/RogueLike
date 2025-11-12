from __future__ import annotations

import logging

from .buildings_repo import write_buildings_instances_json
from .loaders import load_instances
from .spawner_visuals_persistence import persist_spawner_instance_visuals
from .visuals_common import (
    load_buildings_data,
    load_templates_map,
    get_zone,
    get_local_tile,
    extract_visual_fields,
    compute_new_entry,
    update_visuals_map_entry,
)
from .visuals_tags import ensure_spawner_tags_for_existing_instance

logger = logging.getLogger(__name__)


def preflight_validate_spawner_visuals() -> int:
    try:
        instances = load_instances() or []
        b_arr, existing_ids, max_id = load_buildings_data()
        templates, tmap = load_templates_map()

        total_updated = 0
        for inst in instances:
            try:
                vis = inst.get('visuals') if isinstance(inst, dict) else None
                if not isinstance(vis, dict) or not vis:
                    continue
                zone = get_zone(inst)
                local_tile = get_local_tile(inst)
                inst_updated = False
                for key, val in list(vis.items()):
                    cur_iid, tpl_id, visuals_scale = extract_visual_fields(val, cfg=None, key=None, capture_offsets=False)

                    if cur_iid is not None and cur_iid in existing_ids:
                        ensure_spawner_tags_for_existing_instance(
                            b_arr=b_arr,
                            cur_iid=int(cur_iid),
                            inst_id=str(inst.get('id')) if inst.get('id') is not None else None,
                            visuals_scale=visuals_scale,
                        )
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
                        include_spawner_visual_flag=True,
                        max_id=max_id,
                    )
                    b_arr.append(entry)
                    try:
                        write_buildings_instances_json(b_arr)
                        existing_ids.add(int(entry['id']))
                    except Exception:
                        logger.warning("[SpawnerPlacementSystem][preflight] Could not persist buildings_instances for auto-repair")
                    try:
                        update_visuals_map_entry(vis, str(key), val, int(entry['id']), int(tpl_id))
                        inst_updated = True
                    except Exception:
                        pass
                if inst_updated:
                    try:
                        persist_spawner_instance_visuals(str(inst.get('id')) if inst.get('id') is not None else None, vis, ensure_visible_in_game=True)
                        total_updated += 1
                    except Exception:
                        pass
            except Exception:
                continue
        try:
            if total_updated:
                logger.info("[SpawnerPlacementSystem][preflight] Updated %s spawner visuals", total_updated)
        except Exception:
            pass
        return total_updated
    except Exception:
        logger.exception("[SpawnerPlacementSystem][preflight] Failed preflight spawner visuals", exc_info=False)
        return 0
