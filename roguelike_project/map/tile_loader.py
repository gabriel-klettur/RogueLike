# roguelike_project/map/map_loader.py

from roguelike_project.utils.loader import load_image
from roguelike_project.config import DEBUG
import random
from roguelike_project.config import TILE_SIZE
from roguelike_project.map.tile import Tile

def load_tile_images(theme="default"):
    path = "assets/tiles"

    floor_variants = [
        load_image(f"{path}/floor_{i}.png", (TILE_SIZE, TILE_SIZE))
        for i in range(1, 2)
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

    return {
        ".": floor_variants,
        "#": load_image(f"{path}/wall.png", (TILE_SIZE, TILE_SIZE)),
        "D": dungeon_variants,  # Podés mantener esto si lo usás en otras partes
        "O": room_variants,
        "=": tunnel_variants
    }


def load_map_from_text(map_data):
    tile_images = load_tile_images()
    tile_map = []

    for row_idx, row in enumerate(map_data):
        row_tiles = []
        for col_idx, char in enumerate(row):
            if char in tile_images:
                x = col_idx * TILE_SIZE
                y = row_idx * TILE_SIZE

                sprite = (
                    random.choice(tile_images[char])
                    if isinstance(tile_images[char], list)
                    else tile_images[char]
                )

                row_tiles.append(Tile(x, y, char, sprite))
        tile_map.append(row_tiles)

    return tile_map
