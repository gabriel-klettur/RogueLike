# File: roguelike_project/systems/combat/spells/smoke/controller.py

from roguelike_project.systems.combat.spells.smoke.model import SmokeModel

class SmokeController:
    """
    Controlador: actualiza las partículas de humo cada frame.
    """
    def __init__(self, model: SmokeModel):
        self.model = model

    def update(self):
        m = self.model
        # Actualizar cada partícula
        for p in m.particles:
            p.update()
        # Filtrar las que siguen vivas
        m.particles = [p for p in m.particles if p.age < p.lifespan]
