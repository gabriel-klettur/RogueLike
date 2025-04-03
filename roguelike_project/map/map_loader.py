# roguelike_project/map/map_loader.py

import pygame
from roguelike_project.utils.loader import load_image
from roguelike_project.config import DEBUG
import random

from roguelike_project.config import TILE_SIZE

class Tile:
    def __init__(self, x, y, tile_type, sprite):
        self.x = x
        self.y = y
        self.tile_type = tile_type
        self.sprite = sprite
        self.solid = tile_type == "#"
        self.rect = pygame.Rect(x, y, TILE_SIZE, TILE_SIZE)

    def render(self, screen, camera):
        scaled_sprite = pygame.transform.scale(self.sprite, camera.scale((self.sprite.get_width(), self.sprite.get_height())))
        screen.blit(scaled_sprite, camera.apply((self.x, self.y)))

        if self.solid:
            scaled_rect = pygame.Rect(camera.apply(self.rect.topleft), camera.scale(self.rect.size))

            if DEBUG:
                pygame.draw.rect(screen, (255, 255, 0), scaled_rect, 1)            

def load_tile_images():
    floor_variants = [
        load_image(f"assets/tiles/floor_{i}.png", (TILE_SIZE, TILE_SIZE))
        for i in range(1, 8)
    ]
    return {
        ".": floor_variants,
        "#": load_image("assets/tiles/wall.png", (TILE_SIZE, TILE_SIZE))
    }

def load_map_from_text(map_data):
    tile_images = load_tile_images()
    tiles = []

    for row_idx, row in enumerate(map_data):
        for col_idx, char in enumerate(row):
            if char in tile_images:
                x = col_idx * TILE_SIZE
                y = row_idx * TILE_SIZE

                # Si hay múltiples variantes (como "."), elegimos aleatoriamente
                sprite = (
                    random.choice(tile_images[char])
                    if isinstance(tile_images[char], list)
                    else tile_images[char]
                )

                tiles.append(Tile(x, y, char, sprite))
    return tiles