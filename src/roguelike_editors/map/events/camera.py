from __future__ import annotations

import logging
import pygame

from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.config.config_tiles import TILE_SIZE
 

logger = logging.getLogger(__name__)


def handle_zoom(ev: pygame.event.Event, camera, state) -> None:
    mx, my = pygame.mouse.get_pos()
    wx = mx / camera.zoom + camera.offset_x
    wy = my / camera.zoom + camera.offset_y
    # Continuous zoom for the editor: unlimited zoom out, smooth steps, epsilon floor
    z = float(getattr(camera, "zoom", 1.0)) or 1.0
    factor = 1.1
    if ev.y > 0:
        new_z = z * factor
    else:
        # Allow unlimited zoom out, but avoid reaching zero to prevent division-by-zero
        new_z = max(z / factor, 1e-6)
    if abs(new_z - z) > 1e-9:
        camera.zoom = new_z
        camera.offset_x = wx - mx / camera.zoom
        camera.offset_y = wy - my / camera.zoom
        try:
            setattr(state, "tutorial_camera_zoom_changed_pulse", True)
        except Exception:
            pass


def start_panning(ev: pygame.event.Event, camera, state) -> None:
    state.panning = True
    state.pan_start_mouse = ev.pos
    state.pan_start_offset = (camera.offset_x, camera.offset_y)


def update_panning(ev: pygame.event.Event, camera, state) -> None:
    mx, my = ev.pos
    dx = (mx - state.pan_start_mouse[0]) / camera.zoom
    dy = (my - state.pan_start_mouse[1]) / camera.zoom
    camera.offset_x = state.pan_start_offset[0] - dx
    camera.offset_y = state.pan_start_offset[1] - dy
    try:
        setattr(state, "tutorial_camera_panned_pulse", True)
    except Exception:
        pass


def handle_keyboard_pan(camera, state) -> None:
    keys = pygame.key.get_pressed()
    dx = (1 if keys[pygame.K_RIGHT] else 0) - (1 if keys[pygame.K_LEFT] else 0)
    dy = (1 if keys[pygame.K_DOWN] else 0) - (1 if keys[pygame.K_UP] else 0)
    if dx or dy:
        step = 20 / max(camera.zoom, 0.01)
        camera.offset_x += dx * step
        camera.offset_y += dy * step
        try:
            setattr(state, "tutorial_camera_panned_pulse", True)
        except Exception:
            pass


def center_camera_on_zone(camera, zone: str) -> None:
    ox, oy = global_map_settings.zone_offsets[zone]
    zw, zh = global_map_settings.zone_size
    cx = (ox * TILE_SIZE) + (zw * TILE_SIZE) / 2
    cy = (oy * TILE_SIZE) + (zh * TILE_SIZE) / 2
    camera.offset_x = cx - camera.screen_width / (2 * camera.zoom)
    camera.offset_y = cy - camera.screen_height / (2 * camera.zoom)
