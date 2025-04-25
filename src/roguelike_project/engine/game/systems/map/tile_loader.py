# src.roguelike_project/engine/game/systems/map/tile_loader.py

import random
import os
from src.roguelike_project.utils.loader import load_image
from src.roguelike_project.config import TILE_SIZE
from src.roguelike_project.engine.game.systems.map.tile import Tile
from src.roguelike_project.config_tiles import OVERLAY_CODE_MAP, DEFAULT_TILE_MAP


def load_tile_images(theme="default"):
    path = "assets/tiles"

    floor_variants = [
        load_image(f"{path}/floor_{i}.png", (TILE_SIZE, TILE_SIZE))
        for i in range(1, 8)
    ]

    dungeon_variants = [
        load_image(f"{path}/dungeon_{i}.png", (TILE_SIZE, TILE_SIZE))
        for i in range(1, 2)
    ]

    room_variants = [
        load_image(f"{path}/dungeon_{i}.png", (TILE_SIZE, TILE_SIZE))
        for i in range(1, 2)
    ]

    tunnel_variants = [
        load_image(f"{path}/dungeon_c_{i}.png", (TILE_SIZE, TILE_SIZE))
        for i in range(1, 2)
    ]

    # Mapeo de tiles base (carácter → lista o elemento)
    base_map = {
        ".": floor_variants,
        "#": load_image(f"{path}/wall.png", (TILE_SIZE, TILE_SIZE)),
        "D": dungeon_variants,
        "O": room_variants,
        "=": tunnel_variants
    }
    return base_map


def load_map_from_text(map_data, overlay_map=None):
    """
    Crea una matriz de Tile a partir de map_data y overlay_map.
    overlay_map es una matriz 2D de códigos (str) o None.
    Si overlay_map es None, se inicializa vacío.
    """
    height = len(map_data)
    width = len(map_data[0]) if height > 0 else 0

    # Inicializar overlay_map si no existe
    if overlay_map is None:
        overlay_map = [["" for _ in range(width)] for _ in range(height)]

    # Carga sprites base y overlay
    base_images = load_tile_images()
    tiles = []

    for y, row in enumerate(map_data):
        tile_row = []
        for x, char in enumerate(row):
            # Determinar sprite para este tile
            code = overlay_map[y][x]
            if code:
                # Overlay activo: buscar nombre de archivo
                name = OVERLAY_CODE_MAP.get(code)
                if name:
                    sprite = load_image(f"assets/tiles/{name}.png", (TILE_SIZE, TILE_SIZE))
                else:
                    # Código inválido: fallback a base
                    variant = DEFAULT_TILE_MAP.get(char, None)
                    sprite = load_image(f"assets/tiles/{variant}.png", (TILE_SIZE, TILE_SIZE)) if variant else None
            else:
                # Tile base
                imgs = base_images.get(char)
                if imgs is None:
                    sprite = None
                elif isinstance(imgs, list):
                    sprite = random.choice(imgs)
                else:
                    sprite = imgs

            # Crear Tile
            tile = Tile(x * TILE_SIZE, y * TILE_SIZE, char, sprite)
            tile.overlay_code = code
            tile_row.append(tile)
        tiles.append(tile_row)

    return tiles
