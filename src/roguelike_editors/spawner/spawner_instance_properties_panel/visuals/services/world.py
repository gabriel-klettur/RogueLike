from __future__ import annotations

from typing import Any, Generator
import logging

from . import mapping
from . import building_loader

logger = logging.getLogger(__name__)


def get_world(controller):
    try:
        return getattr(getattr(controller.parent.game, 'ecs', None), 'ecs_world', None)
    except AttributeError:
        return None


def iter_building_entities(controller) -> Generator[Any, None, None]:
    world = get_world(controller)
    try:
        for ob in getattr(world, 'buildings', []) or []:
            yield ob
    except AttributeError:
        return


def find_building_entity_by_id(controller, bid: int):
    for ob in iter_building_entities(controller):
        try:
            if getattr(ob, 'id', None) == int(bid):
                return ob
        except (AttributeError, TypeError, ValueError):
            logger.debug("world.find_building_entity_by_id: error iterating", exc_info=True)
            continue
    # Try to load on demand if not found
    try:
        building_loader.ensure_building_loaded(controller, int(bid))
        for ob in iter_building_entities(controller):
            try:
                if getattr(ob, 'id', None) == int(bid):
                    return ob
            except (AttributeError, TypeError, ValueError):
                logger.debug("world.find_building_entity_by_id: error after load", exc_info=True)
                continue
    except (AttributeError, TypeError, ValueError):
        pass
    return None


def get_selected_spawner_id(controller) -> str | None:
    try:
        inst = getattr(controller.parent.model, 'selected_instance', None)
        if isinstance(inst, dict) and inst.get('id') is not None:
            return str(inst.get('id'))
    except (AttributeError, TypeError, ValueError):
        return None
    return None


def find_visual_entity_for_state(controller, state_key: str):
    """Best-effort resolver for the visual entity of a given state.
    Priority:
      1) World object tagged as spawner visual for this spawner and state
      2) Fallback to instance_id mapping if present
    """
    sid = get_selected_spawner_id(controller)
    # 1) Try tags
    if sid is not None:
        for ob in iter_building_entities(controller):
            try:
                if not getattr(ob, '_is_spawner_visual', False):
                    continue
                if str(getattr(ob, 'spawner_instance_id', getattr(ob, 'spawn_id', ''))) != str(sid):
                    continue
                if str(getattr(ob, 'spawner_state_key', '')) == str(state_key):
                    return ob
            except (ValueError, TypeError, KeyError):
                continue
    # 2) Fallback by instance_id from visuals mapping
    try:
        visuals = getattr(controller.parent.model, 'visuals', {}) or {}
        key_map = getattr(controller.parent.model, 'visuals_key_map', {}) or {}
        json_key = key_map.get(state_key, state_key)
        raw = visuals.get(json_key)
        if raw is not None:
            if isinstance(raw, dict):
                bid = int(raw.get('instance_id') or raw.get('id') or raw.get('building_instance_id'))
            else:
                bid = int(raw)
            return find_building_entity_by_id(controller, int(bid))
    except (AttributeError, TypeError, ValueError, KeyError):
        pass
    return None
