import pygame
import math
import random
from roguelike_project.systems.combat.particles.particle import Particle

class SlashArc:
    def __init__(self, x, y, direction):
        self.particles = []
        self.timer = 0
        self.duration = 0.3  # segundos
        self.x = x
        self.y = y
        self.create_arc_particles(direction)

    def create_arc_particles(self, direction):
        base_angle = math.atan2(direction.y, direction.x)
        for i in range(15):  # cantidad de partículas en el arco
            angle = base_angle + random.uniform(-math.pi/4, math.pi/4)
            speed = random.uniform(3, 6)
            size = random.randint(6, 10)
            color = (0, random.randint(150, 200), 255)
            px = self.x + math.cos(angle) * 16
            py = self.y + math.sin(angle) * 16

            self.particles.append(Particle(
                px, py,
                angle=angle,
                speed=speed,
                color=color,
                size=size,
                lifespan=20
            ))

    def update(self):
        self.timer += self.get_delta()
        for p in self.particles:
            p.update()
        self.particles = [p for p in self.particles if p.age < p.lifespan]

    def render(self, screen, camera):
        for p in self.particles:
            p.render(screen, camera)

    def is_finished(self):
        return not self.particles

    def get_delta(self):
        return pygame.time.get_ticks() / 1000 - self.timer
