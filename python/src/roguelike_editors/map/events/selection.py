from __future__ import annotations

import pygame

from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.config.config_tiles import TILE_SIZE
from .utils import screen_to_world


def handle_zone_selection(ev: pygame.event.Event, camera, state) -> bool:
    world_x, world_y = screen_to_world(ev.pos, camera)
    tx = int(world_x) // TILE_SIZE
    ty = int(world_y) // TILE_SIZE

    for zn, (ox, oy) in global_map_settings.zone_offsets.items():
        if zn in ("no zone", "no-zone"):
            continue
        w, h = global_map_settings.zone_size
        if ox <= tx < ox + w and oy <= ty < oy + h:
            now = pygame.time.get_ticks()
            if state.last_click_zone == zn and now - state.last_click_time <= 400:
                state.renaming_zone = zn
                state.rename_input = zn
                pygame.key.set_repeat(200, 30)
                return True
            state.selected_zone = zn
            state.last_click_zone = zn
            state.last_click_time = now
            return True

    return False
