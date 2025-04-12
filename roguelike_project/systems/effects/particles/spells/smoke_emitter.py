# roguelike_project/systems/combat/effects/smoke_emitter.py

import pygame
import random
import math

class SmokeParticle:
    def __init__(self, x, y, color=(200, 200, 200)):
        self.pos = pygame.math.Vector2(x, y)
        self.velocity = pygame.math.Vector2(
            random.gauss(0, 0.3),
            random.gauss(-1.0, 0.3)
        )
        self.acceleration = pygame.math.Vector2()
        self.lifespan = 100.0
        self.size = random.randint(8, 16)
        self.color = color

    def apply_force(self, force):
        self.acceleration += force

    def update(self):
        self.velocity += self.acceleration
        self.pos += self.velocity
        self.lifespan -= 2.5
        self.acceleration *= 0

    def is_dead(self):
        return self.lifespan <= 0

    def render(self, screen, camera):
        if self.is_dead():
            return

        screen_pos = camera.apply((self.pos.x, self.pos.y))
        alpha = max(0, min(255, int(self.lifespan * 2.55)))

        surface = pygame.Surface((self.size, self.size), pygame.SRCALPHA)
        surface.fill((*self.color, alpha))
        screen.blit(surface, screen_pos)


class SmokeEmitter:
    def __init__(self, x, y, color=(200, 200, 200)):
        self.origin = pygame.math.Vector2(x, y)
        self.color = color
        self.particles = []

    def apply_force(self, force):
        for p in self.particles:
            p.apply_force(force)

    def update(self):
        for _ in range(2):  # cantidad de partículas nuevas por frame
            self.particles.append(SmokeParticle(self.origin.x, self.origin.y, self.color))

        for p in self.particles:
            p.update()

        self.particles = [p for p in self.particles if not p.is_dead()]

    def render(self, screen, camera):
        for p in self.particles:
            p.render(screen, camera)
