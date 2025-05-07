import pygame
from roguelike_engine.utils.loader import load_image
import roguelike_engine.config as config

class Obstacle:
    def __init__(self, x, y, sprite_path="assets/objects/rock.png", size=(64, 64)):
        self.x = x
        self.y = y
        self.rect = pygame.Rect(x + 8, y + 8, 48, 48)
        self.sprite = load_image(sprite_path, size)
        self.size = size

    def render(self, screen, camera):
        scaled = pygame.transform.scale(self.sprite, camera.scale(self.size))
        screen.blit(scaled, camera.apply((self.x, self.y)))

        if config.DEBUG:
            scaled_rect = pygame.Rect(
                camera.apply(self.rect.topleft),
                camera.scale(self.rect.size)
            )
            pygame.draw.rect(screen, (255, 0, 0), scaled_rect, 2)
