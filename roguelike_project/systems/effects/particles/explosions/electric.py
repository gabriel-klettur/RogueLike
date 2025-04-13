import random
import math
from roguelike_project.systems.effects.particles.particle import Particle

class ElectricExplosion:
    def __init__(self, x, y, particle_count=35):
        self.x = x
        self.y = y
        self.particles = [
            Particle(
                x, y,
                angle=random.uniform(0, 2 * math.pi),
                speed=random.uniform(3, 6),
                color=random.choice([(0, 255, 255), (150, 255, 255), (255, 255, 255)]),
                size=random.randint(1, 4),
                lifespan=random.randint(10, 20)
            )
            for _ in range(particle_count)
        ]
        self.finished = False

    def update(self):
        for p in self.particles:
            p.update()
        self.particles = [p for p in self.particles if p.age < p.lifespan]
        self.finished = len(self.particles) == 0

    def render(self, screen, camera):        
        for p in self.particles:
            p.render(screen, camera)
