from __future__ import annotations

import pygame
from typing import Dict, Tuple

Color = Tuple[int, int, int]

# Cache cross surfaces by color
_CROSS_CACHE: Dict[Color, pygame.Surface] = {}


def draw_cross(screen: pygame.Surface, x: float, y: float, color: Color = (255, 255, 0)) -> None:
    """Draw a small cross centered at screen-space (x, y).
    Uses a cached surface per color for performance.
    """
    surf = _CROSS_CACHE.get(color)
    if surf is None:
        surf = pygame.Surface((7, 7), flags=pygame.SRCALPHA)
        pygame.draw.line(surf, color, (0, 3), (6, 3))
        pygame.draw.line(surf, color, (3, 0), (3, 6))
        _CROSS_CACHE[color] = surf
    screen.blit(surf, (int(x - 3), int(y - 3)))


def draw_pink_hit(screen: pygame.Surface, camera, wx: float, wy: float) -> None:
    """Draw a high-contrast pink hit marker at world coords (dot + ring).
    - Dot: hot pink
    - Ring: light pink
    """
    sx, sy = camera.apply((wx, wy))
    pygame.draw.circle(screen, (255, 105, 180), (int(sx), int(sy)), 3)
    pygame.draw.circle(screen, (255, 182, 193), (int(sx), int(sy)), 7, 2)
