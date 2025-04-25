# File: roguelike_project/systems/combat/spells/smoke/model.py
import math
import random
from roguelike_project.systems.particles.particle import Particle

class SmokeModel:
    """
    Modelo puro para el efecto de humo (antes "fart_attack").
    Contiene solo estado y generación de partículas.
    """
    def __init__(self, x, y, direction, num_particles=15):
        self.particles: list[Particle] = []
        base_angle = math.atan2(direction.y, direction.x)
        for _ in range(num_particles):
            angle = base_angle + random.uniform(-math.pi/4, math.pi/4)
            speed = random.uniform(3, 6)
            size = random.randint(6, 10)
            color = (0, random.randint(150, 200), 255)
            px = x + math.cos(angle) * 16
            py = y + math.sin(angle) * 16
            p = Particle(px, py, angle, speed, color, size, lifespan=20)
            self.particles.append(p)

    def is_finished(self) -> bool:
        return not self.particles