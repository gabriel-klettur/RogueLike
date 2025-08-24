import pygame
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.ecs.components.abilities.arcane_flame_component import ArcaneFlameComponent
from roguelike_game.ecs.systems.rendering.combat.spells.arcane_flame.view import ArcaneFlameView

class ArcaneFlameRenderSystem:
    """
    Dibuja el fuego arcano basado en ArcaneFlameModel + ArcaneFlameView.
    """
    def __init__(self, perf_log):
        self.perf_log = perf_log
    
    def update(self, world, screen, camera):
        for eid, comp in world.components.get('ArcaneFlameComponent', {}).items():
            view = ArcaneFlameView(comp.model)
            view.render(screen, camera)
