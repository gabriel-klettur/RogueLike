from __future__ import annotations

from typing import List
import pygame

from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.config.config_camera import ALLOWED_ZOOMS

from ..types import CameraLike, StateLike, MapManagerLike, EntitiesLike


def get_custom_debug_lines(
    state: StateLike,
    camera: CameraLike,
    map_manager: MapManagerLike,
    entities: EntitiesLike,
) -> List[str]:
    lines: List[str] = [
        f"Modo: {state.mode}",
        f"Pos: ({round(entities.player.x)}, {round(entities.player.y)})",
    ]
    mx, my = pygame.mouse.get_pos()
    wx = round(mx / camera.zoom + camera.offset_x)
    wy = round(my / camera.zoom + camera.offset_y)
    lines.append(f"Mouse: ({wx}, {wy})")
    tile_col, tile_row = wx // TILE_SIZE, wy // TILE_SIZE
    tile_text = next((t.tile_type for t in map_manager.tiles_in_region if t.rect.collidepoint(wx, wy)), "?")
    lines.append(f"Tile: ({tile_col}, {tile_row}) Tipo: '{tile_text}'")

    # Camera diagnostics
    try:
        z = float(getattr(camera, "zoom", 1.0))
    except Exception:
        z = 1.0
    try:
        ox = float(getattr(camera, "offset_x", 0.0))
    except Exception:
        ox = 0.0
    try:
        oy = float(getattr(camera, "offset_y", 0.0))
    except Exception:
        oy = 0.0
    try:
        sw = int(getattr(camera, "screen_width", 0))
    except Exception:
        sw = 0
    try:
        sh = int(getattr(camera, "screen_height", 0))
    except Exception:
        sh = 0

    vw = (sw / z) if z else 0.0
    vh = (sh / z) if z else 0.0
    cx = ox + vw / 2.0
    cy = oy + vh / 2.0

    try:
        ts_w, ts_h = camera.scale((TILE_SIZE, TILE_SIZE))
    except Exception:
        ts_w, ts_h = int(TILE_SIZE * z), int(TILE_SIZE * z)

    def _fmt_opt(val):
        try:
            if val is None:
                return "n/a"
            f = float(val)
            return f"{f:.5f}"
        except Exception:
            return "n/a"

    min_zoom = getattr(camera, "min_zoom", None)
    max_zoom = getattr(camera, "max_zoom", None)
    zoom_step = getattr(camera, "zoom_step", None)

    lines.append(
        (
            "Camera: " f"zoom={z:.5f} scale={z:.5f} " f"offset=({ox:.5f}, {oy:.5f})"
        )
    )
    lines.append(
        (
            "  screen=" f"{sw}x{sh} world_view=(x0={ox:.5f}, y0={oy:.5f}, w={vw:.5f}, h={vh:.5f})"
        )
    )
    lines.append(
        (
            "  center_world=" f"({cx:.5f}, {cy:.5f}) tile_screen={ts_w}x{ts_h} px_per_world={z:.5f}"
        )
    )
    lines.append(
        (
            "  limits: "
            f"min_zoom={_fmt_opt(min_zoom)} max_zoom={_fmt_opt(max_zoom)} step={_fmt_opt(zoom_step)}"
        )
    )
    try:
        allowed_str = ", ".join(f"{v:.5f}" for v in ALLOWED_ZOOMS)
        lines.append(f"  allowed_zooms: [{allowed_str}]")
    except Exception:
        pass
    return lines
