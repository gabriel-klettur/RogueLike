"""
Calibración de posición de jugador basada en coordenadas de tile.
"""
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_game.factories.player.config import FEET_HEIGHT_DIVISOR


def calibrate_tile_position(tile_x: int, tile_y: int, initial_frame) -> tuple[int, int]:
    """
    Transforma coordenadas en tiles a píxeles alineando 'feet' al centro del tile.
    """
    if initial_frame is None:
        return tile_x * TILE_SIZE, tile_y * TILE_SIZE

    w_img, h_img = initial_frame.get_size()
    feet_height = h_img // FEET_HEIGHT_DIVISOR
    half_feet = feet_height // 2

    cx = tile_x * TILE_SIZE + TILE_SIZE // 2
    cy = tile_y * TILE_SIZE + TILE_SIZE // 2

    px = cx - (w_img // 2)
    py = cy - (h_img - half_feet)
    return px, py
