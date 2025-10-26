from __future__ import annotations

import math
from typing import Tuple
import pygame

from roguelike_engine.config.config_tiles import TILE_SIZE


def build_visibility_mask_lowres(
    lw: int,
    lh: int,
    scale: int,
    camera,
    map_manager,
    light_world_pos: Tuple[float, float],
    max_radius_px: int,
    rays: int = 64,
    step_px: float = None,
) -> pygame.Surface:
    """Return a low-res mask (lw x lh) where visible area from light is white and the rest black.

    This is a coarse visibility fan by raymarching against solid tiles. It's intended
    for a small number of hero lights to keep performance under budget.
    """
    step_px = step_px or max(4.0, TILE_SIZE / 2.0)
    mask = pygame.Surface((lw, lh), flags=pygame.SRCALPHA)
    mask.fill((0, 0, 0, 255))

    # Light center in low-res screen coords
    cx, cy = camera.apply(light_world_pos)
    cx //= scale
    cy //= scale

    tiles = getattr(map_manager, 'tiles', None)
    if tiles is None:
        # No tiles -> nothing blocks: draw full disk
        pygame.draw.circle(mask, (255, 255, 255, 255), (int(cx), int(cy)), max(1, int(max_radius_px / scale)))
        return mask

    rows = len(tiles)
    cols = len(tiles[0]) if rows else 0

    # Helper: returns True if world point is inside solid tile
    def _is_blocked(wx: float, wy: float) -> bool:
        gx = int(wx // TILE_SIZE)
        gy = int(wy // TILE_SIZE)
        if gx < 0 or gy < 0 or gx >= cols or gy >= rows:
            return True  # out of bounds: treat as blocked
        try:
            return bool(getattr(tiles[gy][gx], 'solid', False))
        except Exception:
            return True

    # Sample rays and collect endpoints
    pts = []
    for i in range(rays):
        ang = (i / float(rays)) * math.tau
        dx = math.cos(ang)
        dy = math.sin(ang)
        s = 0.0
        ex = light_world_pos[0]
        ey = light_world_pos[1]
        # March until blocked or max radius
        while s < max_radius_px:
            ex = light_world_pos[0] + dx * s
            ey = light_world_pos[1] + dy * s
            if _is_blocked(ex, ey):
                # Backtrack slightly to the last non-blocked position
                s = max(0.0, s - step_px)
                ex = light_world_pos[0] + dx * s
                ey = light_world_pos[1] + dy * s
                break
            s += step_px
        sx, sy = camera.apply((ex, ey))
        sx //= scale
        sy //= scale
        pts.append((int(sx), int(sy)))

    # Draw fan (center + all endpoints) as white polygon
    try:
        pygame.draw.polygon(mask, (255, 255, 255, 255), [(int(cx), int(cy))] + pts)
    except Exception:
        # Fallback: connect segments
        for p in pts:
            pygame.draw.line(mask, (255, 255, 255, 255), (int(cx), int(cy)), p)
    return mask
