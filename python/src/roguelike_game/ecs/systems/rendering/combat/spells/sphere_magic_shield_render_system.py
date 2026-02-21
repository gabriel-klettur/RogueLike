import pygame
from roguelike_engine.utils.benchmark.benchmark import benchmark
from roguelike_game.ecs.components.abilities.sphere_magic_shield_component import SphereMagicShieldComponent
from roguelike_game.ecs.systems.rendering.combat.spells.sphere_magic_shield.view import SphereMagicShieldView

class SphereMagicShieldRenderSystem:
    """
    ECS system to render magic shield using SphereMagicShieldView.
    """
    def __init__(self, perf_log):
        self.perf_log = perf_log
    
    def update(self, world, screen, camera):
        dirty_rects = []
        for eid, comp in world.components.get('SphereMagicShieldComponent', {}).items():
            view = SphereMagicShieldView(comp.model)
            d = view.render(screen, camera)
            if d: dirty_rects.append(d)
        return dirty_rects
