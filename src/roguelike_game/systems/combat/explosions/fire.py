# Path: src/roguelike_game/systems/combat/explosions/fire.py
import random
import math
from src.roguelike_game.systems.particles.particle import Particle

class FireExplosion:
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