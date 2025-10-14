from __future__ import annotations

import pygame

from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.config.config_tiles import TILE_SIZE
from .utils import screen_to_world


def handle_mode_clicks(ev: pygame.event.Event, camera, state, controller, map_manager) -> bool:
    world_x, world_y = screen_to_world(ev.pos, camera)
    tx = int(world_x) // TILE_SIZE
    ty = int(world_y) // TILE_SIZE

    if state.add_zone_mode:
        if controller.toolbar.add_zone.handle_map_click(tx, ty):
            return True

    if state.delete_zone_mode:
        if controller.toolbar.delete_zone.handle_map_click(tx, ty):
            return True

    if state.paint_tiles_mode:
        for zn, (ox, oy) in global_map_settings.zone_offsets.items():
            if zn in ("no zone", "no-zone"):
                continue
            w, h = global_map_settings.zone_size
            if ox <= tx < ox + w and oy <= ty < oy + h:
                state.pending_paint_tiles_zone = zn
                state.confirm_paint_tiles = True
                state.paint_tiles_mode = False
                return True

    if state.clear_colliders_mode:
        if controller.toolbar.clear_colliders.handle_map_click(tx, ty):
            return True

    if state.paint_colliders_mode:
        if controller.toolbar.paint_colliders.handle_map_click(tx, ty):
            return True

    return False
