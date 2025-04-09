import pygame
import math
import random
from roguelike_project.entities.combat.base.particle import Particle
from roguelike_project.entities.combat.explosions.electric import ElectricExplosion  # 👈 Asegúrate de tener este archivo

class LaserShot:
    def __init__(self, x1, y1, x2, y2, particle_count=60):
        self.particles = []
        self.finished = False

        dx = x2 - x1
        dy = y2 - y1
        distance = math.hypot(dx, dy)
        if distance == 0:
            self.explosion = None
            return

        angle = math.atan2(dy, dx)

        # 🔵 Partículas del láser (a lo largo del rayo)
        for i in range(particle_count):
            t = i / particle_count
            px = x1 + t * dx + random.uniform(-4, 4)
            py = y1 + t * dy + random.uniform(-4, 4)
            color = random.choice([(0, 255, 255), (150, 255, 255), (255, 255, 255)])
            self.particles.append(Particle(
                px, py,
                angle=angle + random.uniform(-0.1, 0.1),
                speed=0,
                color=color,
                size=random.randint(2, 4),
                lifespan=5
            ))

        # ⚡ Explosion eléctrica en el objetivo
        self.explosion = ElectricExplosion(x2, y2)

    def update(self):
        for p in self.particles:
            p.update()
        self.particles = [p for p in self.particles if p.age < p.lifespan]

        if self.explosion:
            self.explosion.update()

        self.finished = len(self.particles) == 0 and (self.explosion is None or self.explosion.finished)

    def render(self, screen, camera):
        for p in self.particles:
            p.render(screen, camera)
        if self.explosion:
            self.explosion.render(screen, camera)
