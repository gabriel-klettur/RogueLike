# teleport_beam.py
import pygame
import random
from pygame.math import Vector2

class TeleportParticle:
    def __init__(self, x, y, color, size=3, lifespan=30):
        self.pos = Vector2(x, y)
        self.velocity = Vector2(
            random.uniform(-0.3, 0.3),   # Leve dispersión horizontal
            random.uniform(-2.5, -1.0)   # Movimiento ascendente vertical
        )
        self.age = 0
        self.lifespan = lifespan
        self.color = color
        self.size = size

    def update(self):
        self.pos += self.velocity
        self.age += 1

    def render(self, screen, camera):
        if self.age >= self.lifespan:
            return

        alpha = int(255 * (1 - self.age / self.lifespan))
        surf = pygame.Surface((self.size, self.size), pygame.SRCALPHA)
        surf.fill((*self.color, alpha))
        screen.blit(surf, camera.apply(self.pos))


class TeleportBeamEffect:
    def __init__(self, x, y, sprite_size=(128, 128), beam_width=60, beam_height=150, duration=1.0):
        """ Efecto visual de haz tipo Star Trek centrado en el jugador """
        self.center_x = x + sprite_size[0] // 2
        self.center_y = y + sprite_size[1] // 2

        self.width = beam_width
        self.height = beam_height
        self.duration_ms = duration * 1000
        self.start_time = pygame.time.get_ticks()

        self.particles = []
        self.finished = False

    def update(self):
        now = pygame.time.get_ticks()

        # Generar partículas si dentro del tiempo
        if now - self.start_time < self.duration_ms:
            for _ in range(10):
                half_w = self.width / 2
                half_h = self.height / 2
                px = self.center_x + random.uniform(-half_w, half_w)
                py = self.center_y + random.uniform(-half_h, half_h)
                color = random.choice([(0, 200, 255), (180, 180, 255), (255, 255, 255)])
                self.particles.append(TeleportParticle(px, py, color))
        else:
            self.finished = len(self.particles) == 0

        # Actualizar partículas
        for p in self.particles:
            p.update()
        self.particles = [p for p in self.particles if p.age < p.lifespan]

    def render(self, screen, camera):
        for p in self.particles:
            p.render(screen, camera)

    def is_finished(self):
        return self.finished
