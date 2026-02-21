from __future__ import annotations

import logging
from typing import Callable

import pygame
from ...services.coords import screen_to_tile
from ...services.persistence import zone_for_global_tile
from ...services.persistence import (
    load_instances_json,
    write_instances_json,
    find_instance_in_json,
    load_spawners_json,
)
from roguelike_game.ecs.systems.spawner.placement.loaders import load_waves
from roguelike_game.ecs.systems.spawner.placement.visuals import auto_repair_state_visuals
from ...services.picking import pick_spawner_under_cursor

from .. import types as etypes
from .mouse_left_building_handles import BuildingHandleInteractions
from .mouse_left_common import LeftClickContext
from .mouse_left_placement import handle_placement_mode, handle_skip_first_placement_click
from .mouse_left_remove import handle_remove_mode
from .mouse_left_selection import (
    handle_anchor_selection,
    handle_building_selection,
    handle_clear_selection,
)


def handle_mousedown_left(h, ctx: etypes.EditorCtx, event: pygame.event.Event) -> bool:
    """Dispatch left mouse button events through modular handler stages."""

    logger = logging.getLogger(__name__)
    context = LeftClickContext(handler=h, editor_ctx=ctx, event=event, logger=logger)

    stage_handlers: tuple[Callable[[LeftClickContext], bool], ...] = (
        handle_skip_first_placement_click,
        handle_placement_mode,
        handle_remove_mode,
        handle_building_selection,
        handle_anchor_selection,
    )

    for handler in stage_handlers:
        if handler(context):
            return True

    if BuildingHandleInteractions(context).run():
        return True

    return handle_clear_selection(context)
