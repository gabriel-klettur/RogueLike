from __future__ import annotations

import pygame
from typing import List, Any, Tuple


def buildings_under_mouse(mouse_pos: Tuple[int, int], camera: Any, buildings: List[Any]) -> List[Any]:
    """Return buildings under the mouse in top-down order (topmost last in list, then reversed).

    Args:
        mouse_pos: (mx, my) in screen coordinates.
        camera: Object with zoom, offset_x, offset_y.
        buildings: Iterable of building-like with x,y,image.get_size().
    """
    mx, my = mouse_pos
    wx = mx / camera.zoom + camera.offset_x
    wy = my / camera.zoom + camera.offset_y
    result: List[Any] = []
    for b in reversed(buildings):
        w, h = b.image.get_size()
        rect = pygame.Rect(b.x, b.y, w, h)
        if rect.collidepoint(wx, wy):
            result.append(b)
    return result


def pick_first_under_mouse(mouse_pos: Tuple[int, int], camera: Any, buildings: List[Any]) -> Any | None:
    """Pick the topmost building under the mouse or None."""
    hits = buildings_under_mouse(mouse_pos, camera, buildings)
    return hits[0] if hits else None
