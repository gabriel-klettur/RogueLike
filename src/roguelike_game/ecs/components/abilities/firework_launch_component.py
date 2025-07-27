"""
ECS component para el lanzamiento de fuegos artificiales.
"""
from roguelike_game.ecs.systems.rendering.combat.spells.firework_launch.model import FireworkLaunchModel

class FireworkLaunchComponent:
    """
    Componente ECS que envuelve el modelo de FireworkLaunch.
    """
    def __init__(self, model: FireworkLaunchModel):
        self.model = model
