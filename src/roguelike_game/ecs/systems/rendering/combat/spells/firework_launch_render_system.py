import pygame
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.ecs.components.abilities.firework_launch_component import FireworkLaunchComponent
from roguelike_game.ecs.systems.rendering.combat.spells.firework_launch.view import FireworkLaunchView

class FireworkLaunchRenderSystem:
    """
    Sistema ECS que renderiza el lanzamiento de fuegos artificiales usando FireworkLaunchView.
    """
    def __init__(self, perf_log):
        self.perf_log = perf_log
    
    def update(self, world, screen: pygame.Surface, camera):
        for eid, comp in world.components.get('FireworkLaunchComponent', {}).items():
            view = FireworkLaunchView(comp.model)
            view.render(screen, camera)
