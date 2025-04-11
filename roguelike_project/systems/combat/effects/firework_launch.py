# roguelike_project/systems/combat/effects/firework_launch.py

import random
import math
from roguelike_project.systems.combat.particles.particle import Particle

class FireworkLaunch:
    def __init__(self, x, y, target_x, target_y):
        self.x = x
        self.y = y
        self.target_x = target_x
        self.target_y = target_y
        self.particles = []
        self.finished = False

        dx = target_x - x
        dy = target_y - y
        self.angle = math.atan2(dy, dx)
        self.speed = 12
        self.distance_travelled = 0
        self.total_distance = math.hypot(dx, dy)

    def update(self):
        if self.distance_travelled < self.total_distance:
            dx = math.cos(self.angle) * self.speed
            dy = math.sin(self.angle) * self.speed
            self.x += dx
            self.y += dy
            self.distance_travelled += math.hypot(dx, dy)

            for _ in range(4):
                offset_angle = self.angle + random.uniform(-0.3, 0.3)
                speed = random.uniform(1, 2)
                color = random.choice([(255, 255, 255), (255, 200, 150)])
                size = random.randint(1, 3)
                self.particles.append(Particle(
                    self.x, self.y,
                    offset_angle,
                    speed,
                    color,
                    size,
                    20
                ))
        else:
            self.finished = True

        for p in self.particles:
            p.update()
        self.particles = [p for p in self.particles if p.age < p.lifespan]

    def render(self, screen, camera):
        for p in self.particles:
            p.render(screen, camera)
