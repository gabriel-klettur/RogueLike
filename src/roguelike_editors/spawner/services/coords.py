from __future__ import annotations

from roguelike_engine.config.config_tiles import TILE_SIZE


def screen_to_tile(camera, sx: int, sy: int, *, tile_size: int = TILE_SIZE) -> tuple[int, int]:
    """Convert screen pixel coords to tile coords using camera offset/zoom."""
    zoom = getattr(camera, 'zoom', 1.0) or 1.0
    wx = sx / zoom + camera.offset_x
    wy = sy / zoom + camera.offset_y
    tx = int(wx // tile_size)
    ty = int(wy // tile_size)
    return tx, ty
