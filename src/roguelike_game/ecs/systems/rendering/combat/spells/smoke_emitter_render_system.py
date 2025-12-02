import pygame
from roguelike_engine.utils.benchmark.benchmark import benchmark
from roguelike_game.ecs.components.abilities.smoke_emitter_component import SmokeEmitterComponent
from roguelike_game.ecs.systems.rendering.combat.spells.smoke_emitter.view import SmokeEmitterView

class SmokeEmitterRenderSystem:
    """
    ECS system that renders SmokeEmitterComponent by delegating to legacy SmokeEmitterView.
    """
    def __init__(self, perf_log):
        self.perf_log = perf_log
    
    def update(self, world, screen, camera):
        for eid, comp in world.components.get('SmokeEmitterComponent', {}).items():
            view = SmokeEmitterView(comp.model)
            view.render(screen, camera)
