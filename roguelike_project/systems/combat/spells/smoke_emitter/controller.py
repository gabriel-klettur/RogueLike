# File: roguelike_project/systems/combat/spells/smoke_emitter/controller.py
from pygame.math import Vector2
from .model import SmokeEmitterModel, SmokeParticle

class SmokeEmitterController:
    """
    Controlador: genera y actualiza partículas de humo.
    """
    def __init__(self, model: SmokeEmitterModel):
        self.model = model

    def apply_force(self, force: Vector2):
        for p in self.model.particles:
            p.apply_force(force)

    def update(self):
        m = self.model
        # Generar nuevas partículas
        for _ in range(m.emit_rate):
            p = SmokeParticle(m.origin.x, m.origin.y, m.color)
            m.particles.append(p)
        # Actualizar y filtrar
        for p in m.particles:
            p.update()
        m.particles = [p for p in m.particles if not p.is_dead()]