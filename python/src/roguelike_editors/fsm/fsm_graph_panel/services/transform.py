from __future__ import annotations

from typing import Tuple, Any


def world_to_screen(x: float, y: float, camera: Any) -> Tuple[int, int]:
    """Project world coords to screen coords using a camera-like object."""
    zoom = getattr(camera, "zoom", 1.0)
    ox = getattr(camera, "offset_x", 0.0)
    oy = getattr(camera, "offset_y", 0.0)
    sx = int(x * zoom + ox)
    sy = int(y * zoom + oy)
    return sx, sy


def screen_to_world(x: int, y: int, camera: Any) -> Tuple[float, float]:
    """Unproject screen coords to world coords using a camera-like object."""
    zoom = getattr(camera, "zoom", 1.0) or 1.0
    ox = getattr(camera, "offset_x", 0.0)
    oy = getattr(camera, "offset_y", 0.0)
    wx = (x - ox) / zoom
    wy = (y - oy) / zoom
    return wx, wy
