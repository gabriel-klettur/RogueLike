import pygame
from roguelike_engine.utils.benchmark import benchmark

from roguelike_game.ecs.components.abilities.puddle_component import PuddleComponent


_DEFAULT_COLORS = {
    'water': (90, 180, 255),
    'poison': (40, 200, 60),
    'acid': (170, 220, 60),
    'lava': (255, 120, 60),
    'ice': (180, 230, 255),
}


class PuddleRenderSystem:
    """
    Renderiza charcos como círculos translúcidos (o decals si existiera un renderer de sprites).
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log

    @benchmark(lambda self: self.perf_log, 'PuddleRenderSystem.update')
    def update(self, world, screen: pygame.Surface, camera):
        pos_map = world.components.get('Position', {})
        puddles = world.components.get('PuddleComponent', {})
        if not puddles:
            return
        for eid, comp in list(puddles.items()):
            pos = pos_map.get(eid)
            if pos is None:
                continue
            # Color y alpha
            color = comp.color or _DEFAULT_COLORS.get((comp.element or '').lower(), (120, 200, 220))
            alpha = max(0, min(255, int(comp.alpha)))
            # Radio escalado por zoom
            radius_px = int(comp.radius * camera.zoom)
            if radius_px <= 0:
                continue
            # Construir surface temporal para alpha
            diam = radius_px * 2
            surf = pygame.Surface((diam, diam), pygame.SRCALPHA)
            pygame.draw.circle(surf, (*color, alpha), (radius_px, radius_px), radius_px)
            # Posicionar en pantalla centrado en el Position
            sx, sy = camera.apply((pos.x - comp.radius, pos.y - comp.radius))
            screen.blit(surf, (int(sx), int(sy)))
