from __future__ import annotations

from typing import Optional

from .mouse_left_common import LeftClickContext


def select_building_under_cursor(context: LeftClickContext) -> Optional[int]:
    building = context.pick_building_under_cursor()
    if building is None:
        return None
    if context.is_building_hidden(building) or not context.is_same_instance(building):
        return None
    bid = getattr(building, "id", None)
    if bid is None:
        return None
    context.set_selected_building_id(int(bid))
    return int(bid)
