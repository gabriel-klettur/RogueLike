from __future__ import annotations

from typing import Iterable, List
import types
import pygame


def build_collision_tiles(
    collision_map: Iterable[Iterable[str]],
    *,
    base_x: int,
    base_y: int,
    tile_size: int,
) -> list[pygame.Rect]:
    """Create a list of pygame.Rect for each '#' tile in the collision map.

    Parameters
    ----------
    collision_map: 2D grid of characters, where '#' means solid.
    base_x, base_y: world-space origin (top-left) for the building.
    tile_size: size of a single tile (pixels).
    """
    rects: list[pygame.Rect] = []
    for row_idx, row in enumerate(collision_map):
        for col_idx, cell in enumerate(row):
            if cell == '#':
                x = base_x + col_idx * tile_size
                y = base_y + row_idx * tile_size
                rects.append(pygame.Rect(x, y, tile_size, tile_size))
    return rects


def build_collision_tile_objs(rects: Iterable[pygame.Rect]) -> list[types.SimpleNamespace]:
    """Wrap rects into lightweight objects exposing `.solid` and `.rect`."""
    return [types.SimpleNamespace(solid=True, rect=r) for r in rects]
