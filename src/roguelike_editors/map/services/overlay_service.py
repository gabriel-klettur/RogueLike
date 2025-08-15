import logging
from roguelike_engine.map.model.layer import Layer
from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.config.config_tiles import TILE_SIZE

logger = logging.getLogger(__name__)


def set_overlay_cell(map_manager, tx: int, ty: int, code: str) -> None:
    """
    Update the in-memory world-sized Ground layer grid at tile coords (tx, ty).
    Bounds-safe and no-op if out of range.
    """
    layers_grid = map_manager.layers.get(Layer.Ground)
    if not layers_grid:
        return
    h = len(layers_grid)
    w = len(layers_grid[0]) if h else 0
    if 0 <= ty < h and 0 <= tx < w:
        layers_grid[ty][tx] = code


def merge_zone_to_world(map_manager, zone: str, zone_grid: list[list[str]]) -> None:
    """
    Merge a zone-sized overlay grid back into the world-sized Ground layer using zone offsets.
    Only non-empty codes are written to avoid accidentally clearing existing overlay.
    """
    world = map_manager.layers.get(Layer.Ground)
    if not world:
        return
    off_x, off_y = global_map_settings.zone_offsets.get(zone, (0, 0))
    hz = len(zone_grid)
    wz = len(zone_grid[0]) if hz else 0
    H = len(world)
    W = len(world[0]) if H else 0

    for yy in range(hz):
        wy = off_y + yy
        if wy < 0 or wy >= H:
            continue
        row = zone_grid[yy]
        for xx in range(wz):
            wx = off_x + xx
            if wx < 0 or wx >= W:
                continue
            code = row[xx]
            if code:
                world[wy][wx] = code
