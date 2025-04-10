# roguelike_project/entities/projectiles/magic_explosion.py

import random
import math
from roguelike_project.systems.combat.particles.particle import Particle

class MagicExplosion:
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

            # Colores mágicos: azul, violeta, celeste, blanco
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
