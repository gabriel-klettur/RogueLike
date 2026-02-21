from typing import List, Optional
import pygame
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.tile.tile_model import Tile
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

    # Precompute sprites for each unique (char, overlay) combination
    keys = {(row[x], overlay_map[y][x]) for y, row in enumerate(map_data) for x in range(len(row))}
    sprite_map = {k: get_sprite_for_tile(k[0], k[1]) for k in keys}

    tiles: List[List[Tile]] = []
    for y, row in enumerate(map_data):
        tile_row: List[Tile] = []
        for x, char in enumerate(row):
            code = overlay_map[y][x]
            sprite = sprite_map[(char, code)]
            # En políticas overlay-only, get_sprite_for_tile puede devolver None.
            # Para mantener la compatibilidad con rutas que dibujan desde tiles_by_layer,
            # usamos una surface totalmente transparente como placeholder.
            if sprite is None:
                try:
                    sprite = pygame.Surface((TILE_SIZE, TILE_SIZE), pygame.SRCALPHA)
                    sprite.fill((0, 0, 0, 0))
                except Exception:
                    sprite = None
            tile = Tile(x * TILE_SIZE, y * TILE_SIZE, char, sprite)
            tile.overlay_code = code
            tile_row.append(tile)
        tiles.append(tile_row)

    return tiles