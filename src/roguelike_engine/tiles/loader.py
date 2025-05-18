
# Path: src/roguelike_engine/tiles/loader.py
from typing import List, Optional
from roguelike_engine.config.config_tiles import TILE_SIZE
from .model import Tile
from .assets import get_sprite_for_tile


def load_tiles_from_text(
    map_data: List[str],
    overlay_map: Optional[List[List[str]]] = None
) -> List[List[Tile]]:
    """
    Transforma una lista de strings y un overlay opcional en una matriz de Tiles.
    """
    height = len(map_data)
    width = len(map_data[0]) if height else 0

    # Inicializar overlay si no existe
    if overlay_map is None:
        overlay_map = [["" for _ in range(width)] for _ in range(height)]

    tiles: List[List[Tile]] = []
    for y, row in enumerate(map_data):
        tile_row: List[Tile] = []
        for x, char in enumerate(row):
            code = overlay_map[y][x]
            sprite = get_sprite_for_tile(char, code)
            tile = Tile(x * TILE_SIZE, y * TILE_SIZE, char, sprite)
            tile.overlay_code = code
            tile_row.append(tile)
        tiles.append(tile_row)

    return tiles