import pygame
from roguelike_engine.utils.benchmark import benchmark

class ParticleRenderSystem:
    """
    ECS system to render particles: dibuja cada partícula como un círculo.
    """
    def __init__(self, perf_log):
        self.perf_log = perf_log
    
    def update(self, world, screen, camera):
        particles = world.components.get('ParticleComponent', {})
        positions = world.components.get('Position', {})
        for eid, comp in list(particles.items()):
            pos = positions.get(eid)
            if pos is None:
                continue
            screen_pos = camera.apply((pos.x, pos.y))
            # Transparencia decreciente
            alpha = int(max(0, 255 * (1 - comp.age / comp.lifespan))) if comp.lifespan > 0 else 255
            # Tamaño según zoom
            size = int(comp.size * getattr(camera, 'zoom', 1))
            # Crear superficie alpha y rellenar con color
            surf = pygame.Surface((size, size), pygame.SRCALPHA)
            surf.fill((*comp.color, alpha))
            # Blit centrado
            x, y = screen_pos
            screen.blit(surf, (int(x - size/2), int(y - size/2)))