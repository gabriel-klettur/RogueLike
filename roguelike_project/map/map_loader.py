# roguelike_project/map/map_loader.py

import pygame
from roguelike_project.utils.loader import load_image

TILE_SIZE = 64

class Tile:
    def __init__(self, x, y, tile_type, sprite):
        self.x = x
        self.y = y
        self.tile_type = tile_type
        self.sprite = sprite
        self.solid = tile_type == "#"
        self.rect = pygame.Rect(x, y, TILE_SIZE, TILE_SIZE)

    def render(self, screen, camera):
        screen.blit(self.sprite, camera.apply((self.x, self.y)))
        if self.solid:
            pygame.draw.rect(screen, (255, 0, 0), camera.apply(self.rect.topleft) + self.rect.size, 1)

def load_tile_images():
    return {
        ".": load_image("assets/tiles/floor.png", (TILE_SIZE, TILE_SIZE)),
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
                tiles.append(Tile(x, y, char, tile_images[char]))
    return tiles
