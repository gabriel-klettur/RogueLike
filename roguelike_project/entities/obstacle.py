import pygame
from utils.loader import load_image
from config import DEBUG

class Obstacle:
    def __init__(self, x, y, sprite_path="assets/objects/rock.png", size=(64, 64)):
        self.x = x
        self.y = y
        self.rect = pygame.Rect(x + 8, y + 8, 48, 48)
        self.sprite = load_image(sprite_path, size)

    def render(self, screen, camera):
        screen.blit(self.sprite, camera.apply((self.x, self.y)))
        if DEBUG:
            pygame.draw.rect(screen, (255, 0, 0), camera.apply(self.rect.topleft) + self.rect.size, 2)