# Path: src/roguelike_game/ecs/systems/particles/particle.py
# Migrated legacy Particle model into ECS folder for explosion effects
import pygame
import random
import math

class Particle:
    """
    ECS-compatible particle model that can be used in explosion and other VFX models.
    x, y: position
    dx, dy: velocity per tick
    color: RGB tuple
    size: pixel size
    lifespan: ticks to live
    age: current age in ticks
    """
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
