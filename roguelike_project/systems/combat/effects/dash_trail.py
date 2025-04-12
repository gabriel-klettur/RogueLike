import pygame
import random
import math
from roguelike_project.systems.combat.particles.particle import Particle

class DashTrail:
    def __init__(self, player, direction):
        self.player = player
        self.direction = direction
        self.particles = []
        self.emit_rate = 0.01  # segundos entre partículas
        self.last_emit = 0
        self.time_alive = 0
        self.active = True  # ✅ bandera para cortar emisión

    def update(self):
        delta = self.player.state.clock.get_time() / 1000
        self.time_alive += delta

        if self.active and self.time_alive - self.last_emit >= self.emit_rate:
            self.emit_particle()
            self.last_emit = self.time_alive

        for p in self.particles:
            p.update()

        self.particles = [p for p in self.particles if p.age < p.lifespan]

    def emit_particle(self):
        px = self.player.x + self.player.sprite_size[0] / 2
        py = self.player.y + self.player.sprite_size[1]

        angle_offset = random.uniform(-0.4, 0.4)
        angle = math.atan2(-self.direction.y, -self.direction.x) + angle_offset
        speed = random.uniform(1, 3)
        size = random.randint(3, 6)
        color = random.choice([
            (200, 200, 255),
            (150, 150, 255),
            (255, 255, 255)
        ])

        self.particles.append(Particle(
            px + random.randint(-5, 5),
            py + random.randint(-5, 5),
            angle=angle,
            speed=speed,
            color=color,
            size=size,
            lifespan=15
        ))

    def stop(self):
        """✅ Detener la emisión de nuevas partículas"""
        self.active = False

    def render(self, screen, camera):
        for p in self.particles:
            p.render(screen, camera)

    def is_finished(self):
        return not self.particles and not self.active
