# File: src.roguelike_game/systems/combat/spells/smoke/controller.py

from src.roguelike_game.systems.combat.spells.smoke.model import SmokeModel

class SmokeController:
    """
    Controlador: actualiza las partículas de humo cada frame.
    """
    def __init__(self, model: SmokeModel):
        self.model = model

    def update(self):
        model = self.model
        # Actualizar cada partícula
        for p in model.particles:
            p.update()
        # Filtrar las que siguen vivas
        model.particles = [p for p in model.particles if p.age < p.lifespan]
