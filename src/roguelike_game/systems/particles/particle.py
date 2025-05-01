# Path: src/roguelike_game/systems/particles/particle.py
import pygame
import random
import math

class Particle:
    def __init__(self, x, y, angle, speed, color, size, lifespan):
        self.x = x
        self.y = y
        self.dx = math.cos(angle) * speed
        self.dy = math.sin(angle) * speed
        self.color = color
        self.size = size
        self.lifespan = lifespan
        self.age = 0

    def update(self):
        self.x += self.dx
        self.y += self.dy
        self.age += 1

    def render(self, screen, camera):
        if self.age >= self.lifespan:
            return
        alpha = max(0, 255 * (1 - self.age / self.lifespan))
        surf = pygame.Surface((self.size, self.size), pygame.SRCALPHA)
        surf.fill((*self.color, int(alpha)))
        screen.blit(surf, camera.apply((self.x, self.y)))