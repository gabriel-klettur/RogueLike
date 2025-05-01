# Path: src/roguelike_game/systems/combat/spells/firework_launch/model.py
import math
from pygame.math import Vector2

class FireworkLaunchModel:
    """
    Modelo puro para el lanzamiento de petarda: posición, destino, recorrido y partículas.
    """
    def __init__(self, x: float, y: float, target_x: float, target_y: float, speed: float = 12):
        self.x = x
        self.y = y
        self.target = Vector2(target_x, target_y)
        # Dirección y distancia total
        delta = self.target - Vector2(x, y)
        self.distance = delta.length()
        self.angle = math.atan2(delta.y, delta.x) if self.distance else 0
        self.speed = speed
        self.traveled = 0.0
        self.particles: list[ParticleData] = []  # ver abajo
        self.finished = False

# Representa datos básicos para crear partículas, la lógica de render se delega en View
class ParticleData:
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