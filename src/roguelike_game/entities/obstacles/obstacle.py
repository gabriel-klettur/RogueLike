# Path: src/roguelike_game/entities/obstacles/obstacle.py
import pygame
from roguelike_engine.utils.loader import load_image
from roguelike_engine.utils.debug import draw_debug_rect

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

        draw_debug_rect(screen, camera, self.rect, color=(255,0,0), width=2)