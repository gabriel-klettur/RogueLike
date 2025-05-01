
# Path: src/roguelike_engine/tiles/model.py
import pygame
from roguelike_engine.config_tiles import TILE_SIZE
from roguelike_engine.config import DEBUG

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

    def render(self, screen: pygame.Surface, camera) -> pygame.Rect | None:
        """
        Dibuja el sprite escalado según la cámara y retorna el rect de colisión.
        """
        if not camera.is_in_view(self.x, self.y, self.sprite_size):
            return None

        zoom = round(camera.zoom * 10) / 10.0
        if zoom not in self.scaled_cache:
            scaled_size = camera.scale(self.sprite_size)
            self.scaled_cache[zoom] = pygame.transform.scale(self.sprite, scaled_size)

        scaled_sprite = self.scaled_cache[zoom]
        screen.blit(scaled_sprite, camera.apply((self.x, self.y)))

        if DEBUG and self.solid:
            scaled_rect = pygame.Rect(
                camera.apply(self.rect.topleft),
                camera.scale(self.rect.size)
            )
            pygame.draw.rect(screen, (255, 255, 0), scaled_rect, 1)

        return self.rect