# Path: src/roguelike_game/systems/combat/explosions/firework_explosion.py
import random
import math
from roguelike_game.systems.particles.particle import Particle

class FireworkExplosion:
    def __init__(self, x, y, particle_count=60):
        self.particles = []
        self.finished = False

        for _ in range(particle_count):
            angle = random.uniform(0, 2 * math.pi)
            speed = random.uniform(2, 5)
            size = random.randint(2, 4)
            lifespan = random.randint(20, 40)
            color = random.choice([
                (255, 100, 100), (100, 255, 255), (255, 255, 100),
                (255, 255, 255), (180, 80, 255), (100, 255, 150)
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