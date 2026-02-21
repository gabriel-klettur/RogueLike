from __future__ import annotations

import pygame
from typing import List

from roguelike_engine.config.config_tiles import TILE_SIZE


class CollisionGridRenderer:
    """Efficient collision-grid overlay renderer.

    Caches font and glyph surfaces per zoom to avoid re-creating them each frame.
    """

    def __init__(self) -> None:
        self._last_zoom: float | None = None
        self._font: pygame.font.Font | None = None
        self._surf_solid: pygame.Surface | None = None
        self._surf_walkable: pygame.Surface | None = None

    def render(self, screen: pygame.Surface, camera, map_) -> List[pygame.Rect]:
        dirty: list[pygame.Rect] = []
        sw, sh = screen.get_size()
        tile_sz = TILE_SIZE
        zoom = camera.zoom
        x_off = camera.offset_x
        y_off = camera.offset_y

        # Determine visible tile range
        col_start = max(0, int(x_off / tile_sz))
        row_start = max(0, int(y_off / tile_sz))
        col_end = min(len(map_.tiles[0]), int((x_off + sw / zoom) / tile_sz) + 1)
        row_end = min(len(map_.tiles), int((y_off + sh / zoom) / tile_sz) + 1)

        # Regenerate text surfaces only on zoom change
        if zoom != self._last_zoom:
            size = max(1, int(14 * zoom))
            self._font = pygame.font.SysFont("Arial", size)
            self._surf_solid = self._font.render("#", True, (255, 0, 0))
            self._surf_walkable = self._font.render(".", True, (200, 200, 200))
            self._last_zoom = zoom

        surf_solid = self._surf_solid
        surf_walk = self._surf_walkable
        assert surf_solid is not None and surf_walk is not None

        # Draw only visible tiles
        for r in range(row_start, row_end):
            for c in range(col_start, col_end):
                tile = map_.tiles[r][c]
                surf = surf_solid if getattr(tile, "solid", False) else surf_walk
                sx = int((c * tile_sz - x_off) * zoom)
                sy = int((r * tile_sz - y_off) * zoom)
                # Center collision symbol in tile
                text_rect = surf.get_rect()
                text_rect.center = (sx + tile_sz * zoom / 2, sy + tile_sz * zoom / 2)
                screen.blit(surf, text_rect.topleft)
                dirty.append(text_rect)
        return dirty
