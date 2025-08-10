"""Camera-space conversion helpers for Entities editor.
"""
from __future__ import annotations

from typing import Tuple


def screen_to_world(camera, sx: int, sy: int) -> Tuple[float, float]:
    """Convert screen coordinates to world coordinates using camera.
    """
    wx = sx / camera.zoom + camera.offset_x
    wy = sy / camera.zoom + camera.offset_y
    return wx, wy


def screen_to_tile(camera, sx: int, sy: int, tile_size: int) -> tuple[int, int]:
    """Convert screen coordinates to tile coordinates.
    """
    wx, wy = screen_to_world(camera, sx, sy)
    tx = int(wx // tile_size)
    ty = int(wy // tile_size)
    return tx, ty
