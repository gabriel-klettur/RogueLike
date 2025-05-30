import pygame
from roguelike_engine.utils.benchmark import benchmark

class FireballRenderSystem:
    """
    Dibuja las fireballs creadas por el ECS como círculos.
    """
    def __init__(self, perf_log, radius=5, color=(255, 100, 0)):
        self.radius = radius
        self.color = color
        self.perf_log = perf_log

    @benchmark(lambda self: self.perf_log, "4.2.2.FireballRenderSystem.update")
    def update(self, world, screen, camera):
        # Itera todas las fireballs y dibuja un círculo en su posición de mundo
        for eid, comp in world.components.get('FireballComponent', {}).items():
            pos = world.components['Position'][eid]
            # Transformar coordenadas mundo → pantalla
            x, y = camera.apply((pos.x, pos.y))
            # Dibujar círculo escalado por el zoom de cámara
            pygame.draw.circle(screen, self.color, (int(x), int(y)), int(self.radius * camera.zoom))
