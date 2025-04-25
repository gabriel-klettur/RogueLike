import math
import random
from roguelike_project.systems.combat.view.effects.particles.particle import Particle

class DashBounce:
    def __init__(self, x, y):
        self.x = x
        self.y = y
        self.particles = []
        self.finished = False

        for _ in range(20):
            angle = random.uniform(0, 2 * math.pi)
            speed = random.uniform(2, 5)
            size = random.randint(3, 6)
            color = random.choice([
                (255, 200, 200),
                (255, 100, 100),
                (255, 255, 100),
            ])

            self.particles.append(Particle(
                x, y,
                angle=angle,
                speed=speed,
                color=color,
                size=size,
                lifespan=random.randint(10, 20)
            ))

    def update(self):
        for p in self.particles:
            p.update()
        self.particles = [p for p in self.particles if p.age < p.lifespan]
        self.finished = len(self.particles) == 0

    def render(self, screen, camera):
        for p in self.particles:
            p.render(screen, camera)

    def is_finished(self):
        return self.finished
