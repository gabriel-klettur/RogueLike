from __future__ import annotations

import logging
from typing import Any, List, Optional

from roguelike_editors.spawner.services.persistence import (
    find_instance_in_json,
    persist_drop,
    remove_visual_refs_by_building_id,
    load_instances_json,
)
from roguelike_editors.spawner.spawner_instance_properties_panel.services.buildings_service import (
    load_buildings_instances as _svc_load_buildings_instances,
    write_buildings_instances as _svc_write_buildings_instances,
)
from roguelike_engine.config.map_config import global_map_settings

logger = logging.getLogger(__name__)


def delete_building(editor: Any, building: Any, buildings: List[Any]) -> None:
    """Delete a building with undo support and cascade persistence cleanup."""
    if not hasattr(editor, "undo_stack"):
        editor.undo_stack = []

    try:
        idx = buildings.index(building)
    except ValueError:
        return

    editor.undo_stack.append((building, idx))
    buildings.remove(building)

    if getattr(editor, "selected_building", None) is building:
        editor.selected_building = None
    if getattr(editor, "hovered_building", None) is building:
        editor.hovered_building = None

    try:
        setattr(editor, "tutorial_deleted_pulse", True)
    except Exception:
        pass

    try:
        bid = getattr(building, "id", None)
        if bid is None:
            return
        try:
            bid_int = int(bid)
        except Exception:
            bid_int = None
        if bid_int is None:
            return

        try:
            removed_refs = remove_visual_refs_by_building_id(int(bid_int))
            if removed_refs:
                logger.info(
                    f"[BuildingsEditor] Cleared {removed_refs} visual refs in spawners for building id={bid_int}"
                )
        except Exception:
            pass

        try:
            data = _svc_load_buildings_instances() or []
            kept = []
            for e in data:
                try:
                    if int(e.get("id")) == int(bid_int):
                        continue
                except Exception:
                    pass
                kept.append(e)
            if len(kept) != len(data):
                _svc_write_buildings_instances(kept)
                logger.info(
                    f"[BuildingsEditor] Removed building instance id={bid_int} from buildings_instances.json"
                )
            else:
                try:
                    data2 = _svc_load_buildings_instances() or []
                    kept2 = []
                    for e in data2:
                        try:
                            if int(e.get("id")) == int(bid_int):
                                continue
                        except Exception:
                            pass
                        kept2.append(e)
                    if len(kept2) != len(data2):
                        _svc_write_buildings_instances(kept2)
                        logger.info(
                            f"[BuildingsEditor] Forced removal retry for id={bid_int} (post spawners cleanup)"
                        )
                except Exception:
                    pass
        except Exception:
            pass

        try:
            cur = _svc_load_buildings_instances() or []
            left = [e for e in cur if str(e.get("id")) == str(bid_int)]
            if left:
                logger.warning(
                    f"[BuildingsEditor] Warning: building id={bid_int} still present after delete attempts ({len(left)} left) → forcing final removal"
                )
                forced = [e for e in cur if str(e.get("id")) != str(bid_int)]
                _svc_write_buildings_instances(forced)
                logger.info(
                    f"[BuildingsEditor] Forced removal succeeded for id={bid_int}"
                )
        except Exception:
            pass
    except Exception:
        pass


def count_spawner_refs(bid: int | str) -> int:
    """Count how many visuals across all spawner instances reference this building id."""
    try:
        bid = int(bid)
    except Exception:
        return 0
    try:
        inst = load_instances_json()
    except Exception:
        inst = []
    count = 0
    for it in (inst or []):
        try:
            vis = it.get("visuals")
            if not isinstance(vis, dict) or not vis:
                continue
            for _k, _v in vis.items():
                try:
                    if isinstance(_v, dict):
                        _vid = _v.get("instance_id") or _v.get("id") or _v.get("building_instance_id")
                        _vid = int(_vid) if _vid is not None else None
                    else:
                        _vid = int(_v)
                except Exception:
                    _vid = None
                if _vid is not None and int(_vid) == int(bid):
                    count += 1
        except Exception:
            continue
    return count


def persist_spawner_drop_on_mouse_up(editor: Any, building: Optional[Any]) -> None:
    """Persist a spawner movement when mouse is released, based on snapshot captured on drag start."""
    if building is None:
        return
    try:
        eid = getattr(building, "_spawner_eid", None)
        world = getattr(building, "_world_ref", None)
        start_entry = getattr(editor, "_spawner_drag_start_entry", None)
        if eid is not None and world is not None:
            persist_drop(world, eid, start_entry, overrides_update=None)
    except Exception:
        pass


def snapshot_spawner_for_drag(editor: Any, building: Any) -> None:
    """Capture spawner snapshot for subsequent persistence on drop."""
    try:
        eid = getattr(building, "_spawner_eid", None)
        world = getattr(building, "_world_ref", None)
        if eid is not None and world is not None:
            comps = getattr(world, "components", {})
            cfg = comps.get("SpawnerConfig", {}).get(eid)
            if cfg is not None:
                zone = cfg.zone
                off_x, off_y = global_map_settings.zone_offsets.get(zone, (0, 0))
                tx, ty = cfg.anchor_tile
                local_start = (int(tx - off_x), int(ty - off_y))
                tpl_id = cfg.template_id
                data, idx, overrides = find_instance_in_json(tpl_id, zone, local_start)
                inst_id = None
                try:
                    if idx is not None:
                        inst_id = data[idx].get("id")
                except Exception:
                    inst_id = None
                editor._spawner_drag_start_entry = {
                    "template_id": tpl_id,
                    "zone": zone,
                    "local_tile": local_start,
                    "overrides": overrides,
                    "index": idx,
                    "id": inst_id,
                }
    except Exception:
        editor._spawner_drag_start_entry = None
