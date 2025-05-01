# Path: src/roguelike_game/systems/combat/spells/smoke_emitter/model.py
import pygame
import random

class SmokeParticle:
    """
    Partícula individual de humo.
    Incluye posición, física básica, vida y render.
    """
    def __init__(self, x: float, y: float, color=(200, 200, 200)):
        self.pos = pygame.math.Vector2(x, y)
        self.velocity = pygame.math.Vector2(random.gauss(0, 0.3), random.gauss(-1.0, 0.3))
        self.acceleration = pygame.math.Vector2(0, 0)
        self.lifespan = 100.0
        self.size = random.randint(8, 16)
        self.color = color

    def apply_force(self, force: pygame.math.Vector2):
        self.acceleration += force

    def update(self):
        # Actualiza física
        self.velocity += self.acceleration
        self.pos += self.velocity
        self.lifespan -= 2.5
        self.acceleration *= 0

    def is_dead(self) -> bool:
        return self.lifespan <= 0

    def render(self, screen, camera):
        if self.is_dead():
            return
        # Aplicar cámara
        screen_pos = camera.apply((self.pos.x, self.pos.y))
        # Alpha según vida restante
        alpha = max(0, min(255, int(self.lifespan * 2.55)))
        surf = pygame.Surface((self.size, self.size), pygame.SRCALPHA)
        surf.fill((*self.color, alpha))
        screen.blit(surf, screen_pos)

class SmokeEmitterModel:
    """
    Modelo para emisor de humo: origen, color de partículas y tasa de emisión.
    """
    def __init__(self, x: float, y: float, color=(200, 200, 200), emit_rate: int = 2):
        self.origin = pygame.math.Vector2(x, y)
        self.color = color
        self.emit_rate = emit_rate  # Partículas generadas por frame
        self.particles: list[SmokeParticle] = []

    def is_empty(self) -> bool:
        return not self.particles