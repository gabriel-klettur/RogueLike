from __future__ import annotations

from typing import List, Tuple

import pygame

Point = Tuple[float, float]


def mask_outline_world(mask: pygame.Mask, world_x: float, world_y: float) -> List[Point]:
    """Convert mask.outline() (local coords) to world coordinates using the top-left world position.
    Returns a list of (x, y) points; may be empty if outline not available.
    """
    outline = mask.outline() or []
    return [(world_x + float(px), world_y + float(py)) for (px, py) in outline]
