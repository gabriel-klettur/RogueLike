"""
ECS component para el efecto de humo.
"""
from roguelike_game.systems.effects.spells.smoke.model import SmokeModel

class SmokeComponent:
    """
    Componente ECS que envuelve el SmokeModel.
    """
    def __init__(self, model: SmokeModel):
        self.model = model
