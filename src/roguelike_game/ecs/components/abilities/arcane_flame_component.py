"""
ECS component para el efecto Arcane Flame.
"""
from roguelike_game.ecs.systems.rendering.combat.spells.arcane_flame.model import ArcaneFlameModel

class ArcaneFlameComponent:
    """
    ECS component wrapping ArcaneFlameModel.
    """
    def __init__(self, model: ArcaneFlameModel):
        self.model = model
