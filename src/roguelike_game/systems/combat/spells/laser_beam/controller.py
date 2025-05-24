# Path: src/roguelike_game/systems/combat/spells/laser_beam/controller.py
import math
from .model import LaserBeamModel

class LaserBeamController:
    """
    Controlador: actualiza estado del modelo cada frame.
    """
    def __init__(self, model: LaserBeamModel):
        self.model = model

    def update(self):
        model = self.model
        # Actualizar partículas
        for p in model.particles:
            p.update()
        model.particles = [p for p in model.particles if p.age < p.lifespan]
        # Actualizar explosión
        if model.explosion:
            model.explosion.update()    
       
        # Marcar finalizado
        if not model.particles and (model.explosion is None or model.explosion.finished):
            model.finished = True