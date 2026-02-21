import math
import random
from pygame.math import Vector2

class FireworkLaunchModel:
    """
    Modelo puro para el lanzamiento de petardas: posición, destino, recorrido y partículas.
    """
    def __init__(self, x: float, y: float, target_x: float, target_y: float, speed: float = 12):
        self.x = x
        self.y = y
        self.target = Vector2(target_x, target_y)
        delta = self.target - Vector2(x, y)
        self.distance = delta.length()
        self.angle = math.atan2(delta.y, delta.x) if self.distance else 0
        self.speed = speed
        self.traveled = 0.0
        self.particles: list[ParticleData] = []
        self.finished = False
        self.exploded = False

    def update(self):
        if self.finished:
            return
        # Explosion stage: update particles after rocket explodes
        if self.exploded:
            for pd in self.particles:
                pd.update_position()
            self.particles = [pd for pd in self.particles if not pd.is_dead()]
            if not self.particles:
                self.finished = True
            return
        # Rocket stage: move and create trail particles
        dx = math.cos(self.angle) * self.speed
        dy = math.sin(self.angle) * self.speed
        self.x += dx
        self.y += dy
        self.traveled += math.hypot(dx, dy)
        # Create trail particles
        for _ in range(4):
            angle_off = self.angle + random.uniform(-0.3, 0.3)
            spd = random.uniform(1, 2)
            color = random.choice([(255,255,255),(255,200,150)])
            size = random.randint(1, 3)
            lifespan = 20
            pd = ParticleData(self.x, self.y, angle_off, spd, color, size, lifespan)
            self.particles.append(pd)
        # Update existing particles
        for pd in self.particles:
            pd.update_position()
        # Remove dead particles
        self.particles = [pd for pd in self.particles if not pd.is_dead()]
        # Trigger explosion when reached target
        if self.traveled >= self.distance:
            for _ in range(30):
                angle_off = random.uniform(0, 2 * math.pi)
                spd = random.uniform(2, 5)
                color = random.choice([(255,255,255),(255,200,150)])
                size = random.randint(2, 4)
                lifespan = 40
                pd = ParticleData(self.x, self.y, angle_off, spd, color, size, lifespan)
                self.particles.append(pd)
            self.exploded = True

class ParticleData:
    """
    Datos básicos para partículas de fuegos artificiales.
    """
    def __init__(self, x, y, angle, speed, color, size, lifespan):
        self.x = x
        self.y = y
        self.angle = angle
        self.speed = speed
        self.color = color
        self.size = size
        self.lifespan = lifespan
        self.age = 0

    def update_position(self):
        self.x += math.cos(self.angle) * self.speed
        self.y += math.sin(self.angle) * self.speed
        self.age += 1

    def is_dead(self):
        return self.age >= self.lifespan
