import pygame
import random
from roguelike_game.systems.effects.spells.smoke_emitter.model import SmokeParticle

class SmokeModel:
    """
    Modelo para un fogonazo de humo: genera una cantidad fija de partículas de una sola vez.
    """
    def __init__(self, x: float, y: float, direction: pygame.math.Vector2, count: int):
        self.particles: list[SmokeParticle] = []
        for _ in range(count):
            p = SmokeParticle(x, y)
            # Aplicar fuerza inicial en la dirección del disparo
            p.apply_force(direction * random.uniform(0.5, 1.5))
            self.particles.append(p)
