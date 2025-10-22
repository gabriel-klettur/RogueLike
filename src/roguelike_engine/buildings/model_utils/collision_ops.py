from __future__ import annotations

from typing import Iterable, List
import types
import pygame


def build_collision_tiles(
    collision_map: Iterable[Iterable[str]],
    *,
    base_x: int,
    base_y: int,
    tile_w: float,
    tile_h: float,
) -> list[pygame.Rect]:
    """Create a list of pygame.Rect for each '#' tile using per-axis tile sizes.

    Parameters
    ----------
    collision_map: 2D grid of characters, where '#' means solid.
    base_x, base_y: world-space origin (top-left) for the building.
    tile_w, tile_h: size in pixels for each grid cell along X/Y (derived from image size / grid cols/rows).
    """
    rects: list[pygame.Rect] = []
    w = max(1, int(tile_w))
    h = max(1, int(tile_h))
    for row_idx, row in enumerate(collision_map):
        for col_idx, cell in enumerate(row):
            if cell == '#':
                # Use truncation for stable alignment with overlay and camera scaling
                x = base_x + int(col_idx * tile_w)
                y = base_y + int(row_idx * tile_h)
                rects.append(pygame.Rect(x, y, w, h))
    return rects


def build_collision_tile_objs(rects: Iterable[pygame.Rect]) -> list[types.SimpleNamespace]:
    """Wrap rects into lightweight objects exposing `.solid` and `.rect`."""
    return [types.SimpleNamespace(solid=True, rect=r) for r in rects]
