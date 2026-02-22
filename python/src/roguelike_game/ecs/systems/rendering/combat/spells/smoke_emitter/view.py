import pygame
from roguelike_game.ecs.systems.rendering.combat.spells.smoke_emitter.model import SmokeEmitterModel

class SmokeEmitterView:
    """
    View for continuous smoke emitter: renders particles updated by SmokeEmitterModel.
    """
    def __init__(self, model: SmokeEmitterModel):
        self.model = model

    def render(self, screen, camera):
        for p in self.model.particles:
            p.render(screen, camera)
