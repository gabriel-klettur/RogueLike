import random
import pygame
from roguelike_engine.utils.benchmark import benchmark

class LightningRenderSystem:
    """
    Renderiza los rayos creados por ECS LightningComponent.
    """
    def __init__(self, perf_log):
        self.perf_log = perf_log

    @benchmark(lambda self: self.perf_log, "4.2.2.LightningRenderSystem.update")
    def update(self, world, screen, camera):
        for eid, comp in world.components.get('LightningComponent', {}).items():
            model = comp.model
            if model.is_finished():
                continue
            # renderizar zigzag con alpha dinámico
            temp = pygame.Surface(screen.get_size(), pygame.SRCALPHA)
            alpha = int(255 * (model.lifetime / model.max_lifetime))
            color = (random.randint(80, 120), random.randint(180, 230), 255, alpha)
            pts = [camera.apply(pt) for pt in model.points]
            for a, b in zip(pts, pts[1:]):
                pygame.draw.line(temp, color, a, b, 2)
            screen.blit(temp, (0, 0))