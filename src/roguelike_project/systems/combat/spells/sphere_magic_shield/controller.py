# src.roguelike_project/systems/combat/spells/sphere_magic_shield/controller.py
import math
from src.roguelike_project.systems.combat.spells.sphere_magic_shield.model import SphereMagicShieldModel

class SphereMagicShieldController:
    """
    Actualiza la lógica del escudo: pulsa el radio
    y desaparece al acabarse el tiempo.
    """
    def __init__(self, model: SphereMagicShieldModel):
        self.model = model

    def update(self):
        # pulso suave: oscila ±10% del radio base
        t = self.model.elapsed()
        pulse = math.sin(t * 4) * 0.1  # frecuencia 4Hz
        self.model.radius = int(self.model.base_radius * (1 + pulse))

    def is_finished(self) -> bool:
        return self.model.is_finished()
