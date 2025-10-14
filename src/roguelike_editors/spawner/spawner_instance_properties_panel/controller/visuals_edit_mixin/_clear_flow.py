from __future__ import annotations

from typing import Any


def clear_visual_for_state_flow(owner: Any, state_key: str) -> None:
    """Remove the visual mapping for a given state and clean JSON files.

    - Removes `visuals[state_key]` from the selected spawner instance and persists
    - If the removed mapping referenced a building instance id, attempts to delete
      the corresponding `buildings_instances.json` entry when it is a tagged spawner visual
      for this owner, or according to `strict_visuals_cleanup` rules.
    - Hides/removes the building in the editor/world if it gets deleted.
    - Rebuilds rows after the operation.
    """
    visuals = dict(getattr(owner.model, 'visuals', {}) or {})
    if not visuals:
        return

    key_map = getattr(owner.model, 'visuals_key_map', {}) or {}
    json_key = key_map.get(state_key, state_key)
    v = visuals.get(json_key)
    if v is None:
        return

    bid = None
    try:
        if isinstance(v, dict):
            bid = int(v.get('instance_id') or v.get('id') or v.get('building_instance_id'))
        else:
            bid = int(v)
    except (AttributeError, TypeError, ValueError):
        bid = None

    visuals.pop(json_key, None)
    owner.model.visuals = visuals
    try:
        if owner.model.selected_instance is not None:
            owner.model.selected_instance['visuals'] = visuals
    except AttributeError:
        pass

    owner._persist_instance()

    try:
        owner._reload_selected_from_json()
    except (AttributeError, OSError, ValueError, TypeError):
        pass

    if bid is not None:
        referenced_elsewhere = False
        try:
            from roguelike_editors.spawner.services.persistence import load_instances_json
            all_inst = load_instances_json()
            for _inst in all_inst or []:
                try:
                    vis = _inst.get('visuals')
                    if not isinstance(vis, dict) or not vis:
                        continue
                    for _k, _v in list(vis.items()):
                        try:
                            if isinstance(_v, dict):
                                _vid = _v.get('instance_id') or _v.get('id') or _v.get('building_instance_id')
                                _vid = int(_vid) if _vid is not None else None
                            else:
                                _vid = int(_v)
                        except Exception:
                            _vid = None
                        if _vid is not None and int(_vid) == int(bid):
                            referenced_elsewhere = True
                            break
                    if referenced_elsewhere:
                        break
                except Exception:
                    continue
        except Exception:
            referenced_elsewhere = False

        data = owner._load_buildings_instances()
        sid = None
        try:
            inst = owner.model.selected_instance or {}
            sid = str(inst.get('id')) if inst.get('id') is not None else None
        except Exception:
            sid = None

        kept = []
        removed = False
        for e in data:
            try:
                eid = int(e.get('id'))
            except Exception:
                kept.append(e)
                continue
            if eid != int(bid):
                kept.append(e)
                continue
            ov = e.get('overrides') if isinstance(e, dict) else None
            is_tagged = False
            try:
                if isinstance(ov, dict) and ov.get('_is_spawner_visual') and (sid is None or str(ov.get('spawner_instance_id')) == str(sid)):
                    is_tagged = True
            except Exception:
                is_tagged = False

            is_root_linked = False
            try:
                if sid is not None and (str(e.get('spawner_instance_id')) == str(sid) or str(e.get('spawn_id')) == str(sid)):
                    is_root_linked = True
            except Exception:
                is_root_linked = False

            if is_tagged or (bool(getattr(owner, 'strict_visuals_cleanup', False)) and (is_root_linked or not referenced_elsewhere)):
                removed = True
                continue
            kept.append(e)

        if removed:
            owner._write_buildings_instances(kept)
            owner._building_index = None
            owner._ensure_buildings_index()
            try:
                owner._remove_building_entity_by_id(int(bid))
            except Exception:
                pass

        owner._build_visuals_rows()
        try:
            owner._log.info(
                f"[InstanceProps] Cleared visual for state={state_key}; removed_building_id={bid}"
            )
        except Exception:
            pass

    return
