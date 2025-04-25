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
        model = self.model
        # Generar nuevas partículas
        for _ in range(model.emit_rate):
            p = SmokeParticle(model.origin.x, model.origin.y, model.color)
            model.particles.append(p)
        # Actualizar y filtrar
        for p in model.particles:
            p.update()
        model.particles = [p for p in model.particles if not p.is_dead()]