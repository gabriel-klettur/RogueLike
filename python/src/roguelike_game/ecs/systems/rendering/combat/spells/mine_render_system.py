import pygame
from roguelike_engine.utils.benchmark.benchmark import benchmark

class MineRenderSystem:
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        self._color = (255, 230, 80)
        self._alpha = 96

    @benchmark(lambda self: self.perf_log, 'MineRenderSystem.update')
    def update(self, world, screen: pygame.Surface, camera):
        mines = world.components.get('MineComponent', {})
        if not mines:
            return
        pos_map = world.components.get('Position', {})
        for eid, comp in list(mines.items()):
            pos = pos_map.get(eid)
            if pos is None:
                continue
            radius = float(getattr(comp, 'trigger_radius', 0.0))
            if radius <= 0:
                continue
            radius_px = int(radius * camera.zoom)
            if radius_px <= 0:
                continue
            diam = radius_px * 2
            surf = pygame.Surface((diam, diam), pygame.SRCALPHA)
            pygame.draw.circle(surf, (*self._color, self._alpha), (radius_px, radius_px), radius_px)
            sx, sy = camera.apply((pos.x - radius, pos.y - radius))
            screen.blit(surf, (int(sx), int(sy)))
