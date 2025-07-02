# Path: src/roguelike_game/ecs/systems/combat/explosions_models.py
import random
import math
from roguelike_game.ecs.systems.particles.particle import Particle

class FireExplosionModel:
    def __init__(self, x, y, particle_count=100):
        self.x = x
        self.y = y
        self.particles = [
            Particle(
                x, y,
                angle=random.uniform(0, 2 * math.pi),
                speed=random.uniform(4, 8),
                color=random.choice([
                    (255, 80, 0), (255, 180, 0), (255, 255, 0)
                ]),
                size=random.randint(6, 10),
                lifespan=random.randint(20, 35)
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

class ElectricExplosionModel:
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

class DarkExplosionModel:
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
        for p in self.particles:
            p.render(screen, camera)

class MagicExplosionModel:
    def __init__(self, x, y, particle_count=30):
        self.x = x
        self.y = y
        self.particles = []
        self.finished = False
        for _ in range(particle_count):
            angle = random.uniform(0, 2 * math.pi)
            speed = random.uniform(2, 6)
            size = random.randint(2, 6)
            lifespan = random.randint(20, 40)
            color = random.choice([
                (100, 100, 255),
                (180, 80, 255),
                (50, 255, 255),
                (255, 255, 255)
            ])
            self.particles.append(Particle(x, y, angle, speed, color, size, lifespan))

    def update(self):
        for p in self.particles:
            p.update()
        self.particles = [p for p in self.particles if p.age < p.lifespan]
        self.finished = len(self.particles) == 0

    def render(self, screen, camera):
        for p in self.particles:
            p.render(screen, camera)
