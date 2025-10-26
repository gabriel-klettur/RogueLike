from __future__ import annotations

import pygame
from math import floor, ceil
from typing import Tuple

from roguelike_engine.config.config_tiles import TILE_SIZE


def build_occlusion_mask(screen_size: Tuple[int, int], camera, map_manager) -> pygame.Surface:
    """Build a screen-space occlusion mask from solid tiles.

    White (255) allows light to pass. Black (0) blocks light. This is a cheap
    approximation (per-tile), intended as an optional attenuation step.
    """
    w, h = int(screen_size[0]), int(screen_size[1])
    mask = pygame.Surface((w, h), flags=pygame.SRCALPHA)
    # Fill white (no occlusion)
    mask.fill((255, 255, 255, 255))

    tiles = getattr(map_manager, 'tiles', None)
    if not tiles:
        return mask

    zoom = float(getattr(camera, 'zoom', 1.0) or 1.0)
    ox = float(getattr(camera, 'offset_x', 0.0) or 0.0)
    oy = float(getattr(camera, 'offset_y', 0.0) or 0.0)

    # Compute visible tile bounds in grid space
    vis_left = int(floor(ox / TILE_SIZE))
    vis_top = int(floor(oy / TILE_SIZE))
    vis_right = int(ceil((ox + w / (zoom if zoom != 0 else 1.0)) / TILE_SIZE))
    vis_bottom = int(ceil((oy + h / (zoom if zoom != 0 else 1.0)) / TILE_SIZE))

    rows = len(tiles)
    cols = len(tiles[0]) if rows else 0

    tl = max(0, vis_left)
    tt = max(0, vis_top)
    tr = min(cols, max(0, vis_right))
    tb = min(rows, max(0, vis_bottom))

    # Draw black rects for solid tiles in screen space
    blk = (0, 0, 0, 255)
    for gy in range(tt, tb):
        row = tiles[gy]
        for gx in range(tl, tr):
            try:
                t = row[gx]
            except Exception:
                continue
            if not getattr(t, 'solid', False):
                continue
            sx = int((gx * TILE_SIZE - ox) * zoom)
            sy = int((gy * TILE_SIZE - oy) * zoom)
            sw = int(TILE_SIZE * zoom)
            sh = int(TILE_SIZE * zoom)
            if sw <= 0 or sh <= 0:
                continue
            r = pygame.Rect(sx, sy, sw, sh)
            # Clamp to screen
            if r.right < 0 or r.bottom < 0 or r.left >= w or r.top >= h:
                continue
            try:
                pygame.draw.rect(mask, blk, r)
            except Exception:
                pass

    return mask
