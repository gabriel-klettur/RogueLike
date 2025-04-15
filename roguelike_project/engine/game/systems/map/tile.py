import pygame

from roguelike_project.config import DEBUG
from roguelike_project.config import TILE_SIZE

class Tile:
    def __init__(self, x, y, tile_type, sprite):
        self.x = x
        self.y = y
        self.tile_type = tile_type
        self.sprite = sprite
        self.sprite_size = sprite.get_size()  # ✅ Cache del tamaño del sprite
        self.solid = tile_type == "#"
        self.rect = pygame.Rect(x, y, TILE_SIZE, TILE_SIZE)
        self.scaled_cache = {}  # ✅ Cache de imágenes escaladas por zoom

    def render(self, screen, camera):
        if camera is None:
            screen.blit(self.sprite, (self.x, self.y))
            return pygame.Rect(self.x, self.y, TILE_SIZE, TILE_SIZE)

        if not camera.is_in_view(self.x, self.y, (TILE_SIZE, TILE_SIZE)):
            return None

        zoom = round(camera.zoom * 10) / 10  # ✅ Cache con menor precisión (1 decimal)

        if zoom not in self.scaled_cache:
            self.scaled_cache[zoom] = pygame.transform.scale(
                self.sprite,
                camera.scale(self.sprite_size)
            )

        scaled_sprite = self.scaled_cache[zoom]
        screen.blit(scaled_sprite, camera.apply((self.x, self.y)))

        if self.solid and DEBUG:
            scaled_rect = pygame.Rect(
                camera.apply(self.rect.topleft),
                camera.scale(self.rect.size)
            )
            pygame.draw.rect(screen, (255, 255, 0), scaled_rect, 1)

        return self.rect
