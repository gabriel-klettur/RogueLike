import pygame
from roguelike_engine.utils.benchmark.benchmark import benchmark
from roguelike_game.ecs.systems.rendering.render_system import RenderSystem

class GrayscaleRenderSystem:
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        self._rs = None  # Reuse RenderSystem instance to keep grayscale buffers across frames

    def update(self, world, screen, camera):
        # Aplicar escala de grises si el jugador está muerto
        grays = world.components.get('GrayscaleComponent', {})
        if world.player_entity not in grays:
            return
        # Pasar perf_log para registrar métricas finas desde RenderSystem.apply_grayscale
        if self._rs is None or getattr(self._rs, 'screen', None) is not screen:
            self._rs = RenderSystem(screen)
        self._rs.apply_grayscale(screen, perf_log=getattr(world, 'perf_log', None))
