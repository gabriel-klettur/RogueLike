# Path: roguelike_project/entities/projectiles/base.py

import pygame
import math

class Projectile:
    def __init__(self, x, y, angle, speed, lifespan, sprite, size=(32, 32)):
        self.x = x
        self.y = y
        self.angle = angle  # En grados
        self.speed = speed
        self.lifespan = lifespan  # En frames
        self.sprite = pygame.transform.scale(sprite, size)
        self.size = size

        # Calcular vector de movimiento
        radians = math.radians(angle)
        self.dx = math.cos(radians) * speed
        self.dy = math.sin(radians) * speed

        self.rect = pygame.Rect(self.x, self.y, *self.size)
        self.alive = True

    def update(self):
        # Mover
        self.x += self.dx
        self.y += self.dy
        self.lifespan -= 1

        # Actualizar rectángulo
        self.rect.topleft = (self.x, self.y)

        if self.lifespan <= 0:
            self.alive = False

    def render(self, screen, camera):
        screen.blit(self.sprite, camera.apply((self.x, self.y)))

        # Opcional: debug del rectángulo de colisión
        # pygame.draw.rect(screen, (255, 0, 0), camera.apply(self.rect), 1)
