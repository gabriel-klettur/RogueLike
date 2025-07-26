import pygame
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.ecs.components.abilities.smoke_component import SmokeComponent
from roguelike_game.ecs.systems.rendering.combat.spells.smoke.view import SmokeView

class SmokeRenderSystem:
    """
    ECS system that renders smoke effects using SmokeView.
    """
    def __init__(self, perf_log):
        self.perf_log = perf_log

    @benchmark(lambda self: self.perf_log, "4.2.2.SmokeRenderSystem.update")
    def update(self, world, screen: pygame.Surface, camera):
        for eid, comp in world.components.get('SmokeComponent', {}).items():
            view = SmokeView(comp.model)
            view.render(screen, camera)
