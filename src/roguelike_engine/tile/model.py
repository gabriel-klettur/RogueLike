
# Path: src/roguelike_engine/tile/model.py
import pygame
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.config.config import DEBUG

class Tile:
    """
    Representa un tile del mapa: posición, tipo, sprite y colisión.
    """
    def __init__(
        self,
        x: int,
        y: int,
        tile_type: str,
        sprite: pygame.Surface
    ):
        self.x = x
        self.y = y
        self.tile_type = tile_type
        self.sprite = sprite
        self.sprite_size = sprite.get_size() if sprite else (TILE_SIZE, TILE_SIZE)
        self.solid = (tile_type == "#")
        self.rect = pygame.Rect(x, y, TILE_SIZE, TILE_SIZE)
        self.scaled_cache: dict[float, pygame.Surface] = {}