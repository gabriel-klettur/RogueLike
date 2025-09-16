from __future__ import annotations

import pygame

from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.map.utils import calculate_dungeon_offset

from ..model import DiagnosticsOverlayModel
from ..types import CameraLike, MapManagerLike


def draw_borders(
    screen: pygame.Surface,
    camera: CameraLike,
    map_manager: MapManagerLike,
    model: DiagnosticsOverlayModel,
) -> None:
    """Render zone borders (lobby, dungeon, global) using camera transforms."""
    # Lobby
    x0, y0 = map_manager.lobby_offset
    tl = camera.apply((x0 * TILE_SIZE, y0 * TILE_SIZE))
    sz = camera.scale((global_map_settings.zone_width * TILE_SIZE, global_map_settings.zone_height * TILE_SIZE))
    pygame.draw.rect(screen, model.border_colors["lobby"], pygame.Rect(tl, sz), model.border_width)
    # Dungeon
    dx, dy = calculate_dungeon_offset(map_manager.lobby_offset)
    tl2 = camera.apply((dx * TILE_SIZE, dy * TILE_SIZE))
    sz2 = camera.scale((global_map_settings.zone_width * TILE_SIZE, global_map_settings.zone_height * TILE_SIZE))
    pygame.draw.rect(screen, model.border_colors["dungeon"], pygame.Rect(tl2, sz2), model.border_width)
    # Global
    tl3 = camera.apply((0, 0))
    sz3 = camera.scale((global_map_settings.global_width * TILE_SIZE, global_map_settings.global_height * TILE_SIZE))
    pygame.draw.rect(screen, model.border_colors["global"], pygame.Rect(tl3, sz3), model.border_width)
