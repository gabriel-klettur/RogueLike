from __future__ import annotations

from typing import Iterable, List
import types
import pygame


def build_collision_tiles(
    collision_map: Iterable[Iterable[str]],
    *,
    base_x: int,
    base_y: int,
    tile_size: float | None = None,
    tile_w: float | None = None,
    tile_h: float | None = None,
) -> list[pygame.Rect]:
    """Create a list of pygame.Rect for each '#' tile using per-axis tile sizes.

    Parameters
    ----------
    collision_map: 2D grid of characters, where '#' means solid.
    base_x, base_y: world-space origin (top-left) for the building.
    tile_w, tile_h: size in pixels for each grid cell along X/Y (derived from image size / grid cols/rows).
    """
    rects: list[pygame.Rect] = []
    # Support either square tiles via tile_size or per-axis sizes via tile_w/tile_h
    if tile_size is not None:
        tw = float(tile_size)
        th = float(tile_size)
    else:
        tw = float(tile_w if tile_w is not None else 1.0)
        th = float(tile_h if tile_h is not None else 1.0)
    w = max(1, int(tw))
    h = max(1, int(th))
    for row_idx, row in enumerate(collision_map):
        for col_idx, cell in enumerate(row):
            if cell == '#':
                # Use truncation for stable alignment with overlay and camera scaling
                x = base_x + int(col_idx * tw)
                y = base_y + int(row_idx * th)
                rects.append(pygame.Rect(x, y, w, h))
    return rects


def build_collision_tile_objs(rects: Iterable[pygame.Rect]) -> list[types.SimpleNamespace]:
    """Wrap rects into lightweight objects exposing `.solid` and `.rect`."""
    return [types.SimpleNamespace(solid=True, rect=r) for r in rects]
