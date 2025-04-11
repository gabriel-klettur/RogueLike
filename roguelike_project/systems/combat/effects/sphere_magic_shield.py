import pygame
import random
import math

class ShieldParticle:
    def __init__(self, center, radius):
        angle = random.uniform(0, 2 * math.pi)
        self.offset = pygame.Vector2(
            math.cos(angle) * radius,
            math.sin(angle) * radius
        )
        self.age = 0
        self.lifespan = 90
        self.size = random.randint(3, 5)
        self.color = (100, 200, 255)

    def update(self):
        self.age += 1

    def is_dead(self):
        return self.age >= self.lifespan

    def render(self, screen, camera, center):
        pos = center + self.offset
        alpha = max(0, 255 * (1 - self.age / self.lifespan))
        surf = pygame.Surface((self.size, self.size), pygame.SRCALPHA)
        surf.fill((*self.color, int(alpha)))
        screen.blit(surf, camera.apply(pos))


class SphereMagicShield:
    def __init__(self, player):
        self.player = player
        self.particles = []
        self.frame = 0
        self.expired = False  # ⛔ Deja de generar nuevas partículas al expirar

    def update(self):
        self.frame += 1
        center = pygame.Vector2(
            self.player.x + self.player.sprite_size[0] / 2,
            self.player.y + self.player.sprite_size[1] / 2
        )

        # Solo generar partículas si el escudo sigue activo
        if not self.expired and self.player.stats.is_shield_active():
            for _ in range(5):
                radius = random.uniform(40, 70)
                self.particles.append(ShieldParticle(center, radius))
        else:
            self.expired = True  # 🔚 Dejar de generar nuevas partículas

        for p in self.particles:
            p.update()

        self.particles = [p for p in self.particles if not p.is_dead()]

    def render(self, screen, camera):
        center = pygame.Vector2(
            self.player.x + self.player.sprite_size[0] / 2,
            self.player.y + self.player.sprite_size[1] / 2
        )

        for p in self.particles:
            p.render(screen, camera, center)

        return None

    def is_finished(self):
        return self.expired and len(self.particles) == 0
