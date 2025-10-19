from __future__ import annotations

import logging

from . import world as world_svc
from . import mapping as mapping_svc

logger = logging.getLogger(__name__)


def set_building_visible(controller, bid: int, visible: bool) -> None:
    # Cache intended editor visibility
    try:
        controller.model.editor_visibility[int(bid)] = bool(visible)
    except Exception:
        pass
    ob = world_svc.find_building_entity_by_id(controller, int(bid))
    if ob is not None:
        try:
            setattr(ob, 'editor_hidden', not bool(visible))
        except Exception:
            pass


def is_visible_for_state(controller, state_key: str) -> bool:
    ob = world_svc.find_visual_entity_for_state(controller, state_key)
    if ob is not None:
        try:
            hidden = bool(getattr(ob, 'editor_hidden', False))
            vis = bool(getattr(ob, 'visible', True)) and not hidden
            try:
                bid = getattr(ob, 'id', None)
                if bid is not None:
                    controller.model.editor_visibility[int(bid)] = vis
            except Exception:
                pass
            return vis
        except Exception:
            return True
    bid_int = mapping_svc.get_instance_id_for_state(controller, state_key)
    if bid_int is None:
        return True
    try:
        return bool(controller.model.editor_visibility.get(int(bid_int), True))
    except Exception:
        return True


def toggle_for_state(controller, state_key: str) -> None:
    ob = world_svc.find_visual_entity_for_state(controller, state_key)
    if ob is not None:
        try:
            cur = (not bool(getattr(ob, 'editor_hidden', False)))
            new_vis = not cur
            try:
                setattr(ob, 'editor_hidden', not bool(new_vis))
            except Exception:
                pass
            try:
                bid = getattr(ob, 'id', None)
                if bid is not None:
                    controller.model.editor_visibility[int(bid)] = bool(new_vis)
            except Exception:
                pass
            return
        except Exception:
            pass
    bid_int = mapping_svc.get_instance_id_for_state(controller, state_key)
    if bid_int is None:
        return
    cur = bool(controller.model.editor_visibility.get(int(bid_int), True))
    set_building_visible(controller, int(bid_int), not cur)
