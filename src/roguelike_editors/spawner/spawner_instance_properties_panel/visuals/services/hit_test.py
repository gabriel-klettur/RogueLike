from __future__ import annotations

from typing import Any, List
import logging

from roguelike_editors.spawner.services import load_instances_json as load_spawners_instances_json
from roguelike_editors.spawner.spawner_instance_properties_panel.services.buildings_service import (
    load_buildings_instances,
)

from . import world as world_svc
from . import building_loader as loader_svc
from . import mapping as mapping_svc

logger = logging.getLogger(__name__)


def _screen_bounds(controller, ob) -> tuple[int, int, int, int] | None:
    cam = getattr(controller.parent.game, 'camera', None)
    if cam is None:
        return None
    try:
        x = getattr(ob, 'x', getattr(getattr(ob, 'model', ob), 'x', None))
        y = getattr(ob, 'y', getattr(getattr(ob, 'model', ob), 'y', None))
        img = getattr(ob, 'image', getattr(getattr(ob, 'model', ob), 'image', None))
        if x is None or y is None or img is None:
            return None
        w, h = img.get_size()
        sx, sy = cam.apply((x, y))
        sw, sh = cam.scale((w, h))
        return int(sx), int(sy), int(sw), int(sh)
    except (AttributeError, TypeError, ValueError):
        return None


def pick_building_under_cursor(controller, mx: int, my: int):
    cam = getattr(controller.parent.game, 'camera', None)
    if cam is None:
        return None
    sid = world_svc.get_selected_spawner_id(controller)

    # 1) Prefer tagged entities for this spawner id
    for ob in world_svc.iter_building_entities(controller):
        try:
            if not getattr(ob, '_is_spawner_visual', False):
                continue
            if sid is not None:
                if str(getattr(ob, 'spawner_instance_id', getattr(ob, 'spawn_id', ''))) != str(sid):
                    continue
            b = _screen_bounds(controller, ob)
            if b is None:
                continue
            sx, sy, sw, sh = b
            if sx <= mx <= sx + sw and sy <= my <= sy + sh:
                return ob
        except (AttributeError, TypeError, ValueError):
            continue

    # 2) Fallback: check ids in visuals mapping
    try:
        visuals = getattr(controller.parent.model, 'visuals', {}) or {}
    except (AttributeError, TypeError):
        visuals = {}

    ids: List[int] = []
    for v in visuals.values():
        try:
            if isinstance(v, dict):
                bid = int(v.get('instance_id') or v.get('id') or v.get('building_instance_id'))
            else:
                bid = int(v)
            ids.append(bid)
        except (AttributeError, TypeError, ValueError):
            continue

    for bid in ids:
        try:
            ob = world_svc.find_building_entity_by_id(controller, int(bid))
            if ob is None:
                loader_svc.ensure_building_loaded(controller, int(bid))
                ob = world_svc.find_building_entity_by_id(controller, int(bid))
            if ob is None:
                continue
            b = _screen_bounds(controller, ob)
            if b is None:
                continue
            sx, sy, sw, sh = b
            if sx <= mx <= sx + sw and sy <= my <= sy + sh:
                return ob
        except (AttributeError, TypeError, ValueError):
            continue

    # 3) Last fallback: any building under cursor that is spawner-linked by disk data
    for ob in world_svc.iter_building_entities(controller):
        try:
            b = _screen_bounds(controller, ob)
            if b is None:
                continue
            sx, sy, sw, sh = b
            if not (sx <= mx <= sx + sw and sy <= my <= sy + sh):
                continue
            bid = getattr(ob, 'id', None)
            if bid is None:
                continue
            if is_spawner_visual_building_id(controller, int(bid)):
                return ob
        except (AttributeError, TypeError, ValueError):
            logger.debug("hit_test.pick_building_under_cursor: error in last fallback", exc_info=True)
            continue

    return None


def is_spawner_visual_building_id(controller, bid: int) -> bool:
    # 1) buildings_instances.json overrides
    try:
        for e in load_buildings_instances():
            try:
                if int(e.get('id')) != int(bid):
                    continue
                ov = e.get('overrides') or {}
                if isinstance(ov, dict):
                    if bool(ov.get('_is_spawner_visual', False)):
                        return True
                if e.get('spawner_instance_id') is not None or e.get('spawn_id') is not None:
                    return True
                break
            except (AttributeError, TypeError, ValueError):
                continue
    except (OSError, AttributeError, TypeError, ValueError):
        pass

    # 2) spawners_instances.json visuals mapping
    try:
        arr = load_spawners_instances_json() or []
    except (OSError, AttributeError, TypeError, ValueError):
        logger.debug("hit_test.is_spawner_visual_building_id: failed to load spawners_instances.json", exc_info=True)
        arr = []

    try:
        for inst in arr:
            try:
                vis = inst.get('visuals') if isinstance(inst, dict) else None
                if not isinstance(vis, dict):
                    continue
                for v in vis.values():
                    try:
                        if isinstance(v, dict):
                            vid = int(v.get('instance_id') or v.get('id') or v.get('building_instance_id'))
                        else:
                            vid = int(v)
                    except (AttributeError, TypeError, ValueError):
                        continue
                    if vid == int(bid):
                        return True
            except (AttributeError, TypeError, ValueError):
                logger.debug("hit_test.is_spawner_visual_building_id: error scanning visuals mapping", exc_info=True)
                continue
    except (AttributeError, TypeError, ValueError):
        logger.debug("hit_test.is_spawner_visual_building_id: unexpected error scanning spawners_instances", exc_info=True)

    return False
