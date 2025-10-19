from __future__ import annotations

from typing import Tuple


def screen_to_world(pos: tuple[int, int], camera) -> Tuple[float, float]:
    """Convert screen coordinates to world coordinates using camera zoom/offset."""
    mx, my = pos
    return mx / camera.zoom + camera.offset_x, my / camera.zoom + camera.offset_y
