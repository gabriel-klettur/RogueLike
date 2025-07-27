from roguelike_engine.config.config_tiles import TILE_SIZE

def screen_to_world(mouse_pos, camera):
    """
    Convert screen coordinates to world coordinates based on camera.
    """
    mx, my = mouse_pos
    return mx / camera.zoom + camera.offset_x, my / camera.zoom + camera.offset_y


def world_to_tile(world_pos):
    """
    Convert world coordinates to tile indices (col, row).
    """
    wx, wy = world_pos
    return int(wx) // TILE_SIZE, int(wy) // TILE_SIZE


def screen_to_tile(mouse_pos, camera):
    """
    Convert screen mouse position to tile indices (col, row).
    """
    world = screen_to_world(mouse_pos, camera)
    return world_to_tile(world)
