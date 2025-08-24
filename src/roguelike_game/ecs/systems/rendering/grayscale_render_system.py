import pygame
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.ecs.systems.rendering.render_system import RenderSystem

class GrayscaleRenderSystem:
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
    
    def update(self, world, screen, camera):
        # Aplicar escala de grises si el jugador está muerto
        grays = world.components.get('GrayscaleComponent', {})
        if world.player_entity not in grays:
            return
        RenderSystem(screen).apply_grayscale(screen)
