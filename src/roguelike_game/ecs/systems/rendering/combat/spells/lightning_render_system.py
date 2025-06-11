import pygame
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.ecs.components.abilities.lightning_component import LightningComponent
from roguelike_game.systems.effects.spells.lightning.view import LightningView

class LightningRenderSystem:
    """
    Renderiza los rayos creados por ECS LightningComponent.
    """
    def __init__(self, perf_log):
        self.perf_log = perf_log

    @benchmark(lambda self: self.perf_log, "4.2.2.LightningRenderSystem.update")
    def update(self, world, screen, camera):
        for eid, comp in world.components.get('LightningComponent', {}).items():
            # Usar LightningView para dibujar el rayo
            view = LightningView(comp.model)
            view.render(screen, camera)
