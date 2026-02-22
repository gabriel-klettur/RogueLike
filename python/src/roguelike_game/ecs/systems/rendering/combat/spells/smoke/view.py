import pygame
from roguelike_game.ecs.systems.rendering.combat.spells.smoke.model import SmokeModel

class SmokeView:
    """
    View for a single-instance smoke effect: renders particles managed by SmokeModel.
    """
    def __init__(self, model: SmokeModel):
        self.model = model

    def render(self, screen, camera):
        for p in self.model.particles:
            p.render(screen, camera)
