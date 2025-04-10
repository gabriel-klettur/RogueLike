import random
import math
from roguelike_project.systems.combat.base.particle import Particle

class DarkExplosion:
    def __init__(self, x, y, particle_count=25):
        self.x = x
        self.y = y
        self.particles = [
            Particle(
                x, y,
                angle=random.uniform(0, 2 * math.pi),
                speed=random.uniform(1, 3),
                color=random.choice([(40, 0, 40), (60, 0, 60), (20, 20, 20)]),
                size=random.randint(5, 10),
                lifespan=random.randint(40, 60)
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
        print(f"🧨 Dibujando {len(self.particles)} partículas Magicas Dark en ({self.x}, {self.y})")  # Debug opcional
        for p in self.particles:
            p.render(screen, camera)
