from __future__ import annotations

from typing import Optional, Tuple
from .buildings_repo import write_buildings_instances_json


def ensure_spawner_tags_for_existing_instance(
    b_arr: list[dict], cur_iid: int, inst_id: str | None, visuals_scale: Optional[Tuple[int, int]]
) -> None:
    """Ensure existing instance has proper spawner tags and optional scale override."""
    try:
        changed_bi = False
        for e in b_arr:
            try:
                if int(e.get('id')) != int(cur_iid):
                    continue
            except Exception:
                continue
            if not bool(e.get('spawner_visual', False)):
                e['spawner_visual'] = True
                changed_bi = True
            ov = e.get('overrides') or {}
            if not isinstance(ov, dict):
                ov = {}
            if not bool(ov.get('_is_spawner_visual', False)):
                ov['_is_spawner_visual'] = True
                changed_bi = True
            if inst_id:
                if str(e.get('spawn_id') or '') != inst_id:
                    e['spawn_id'] = inst_id
                    changed_bi = True
                if str(e.get('spawner_instance_id') or '') != inst_id:
                    e['spawner_instance_id'] = inst_id
                    changed_bi = True
                if str((ov or {}).get('spawner_instance_id') or '') != inst_id:
                    ov['spawner_instance_id'] = inst_id
                    changed_bi = True
            if visuals_scale is not None:
                try:
                    cur_sc = ov.get('scale')
                    cur_sc_t = (int(cur_sc[0]), int(cur_sc[1])) if isinstance(cur_sc, (list, tuple)) and len(cur_sc) == 2 else None
                except Exception:
                    cur_sc_t = None
                if cur_sc_t != visuals_scale:
                    ov['scale'] = [int(visuals_scale[0]), int(visuals_scale[1])]
                    changed_bi = True
            e['overrides'] = ov
            break
        if changed_bi:
            write_buildings_instances_json(b_arr)
    except Exception:
        pass
