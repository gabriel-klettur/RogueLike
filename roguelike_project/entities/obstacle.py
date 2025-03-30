import pygame
from utils.loader import load_image
from config import DEBUG

class Obstacle:
    def __init__(self, x, y, sprite_path="assets/objects/rock.png", size=(64, 64)):
        self.x = x
        self.y = y
        self.rect = pygame.Rect(x + 8, y + 8, 48, 48)
        self.sprite = load_image(sprite_path, size)

    def render(self, screen):
        screen.blit(self.sprite, (self.x, self.y))

        if DEBUG:
            # Dibujar rectángulo de colisión en rojo
            pygame.draw.rect(screen, (255, 0, 0), self.rect, 2)