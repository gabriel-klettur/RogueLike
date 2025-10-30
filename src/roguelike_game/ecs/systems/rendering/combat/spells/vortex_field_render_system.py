import pygame
from roguelike_engine.utils.benchmark import benchmark


class VortexFieldRenderSystem:
    """
    Renderiza el área del vortex (ForceFieldComponent) como un círculo translúcido.
    - pull: cian
    - push: naranja/rojo
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        self.colors_pull = [(60, 200, 255), (80, 220, 255), (40, 160, 240)]
        self.colors_push = [(255, 120, 60), (255, 90, 40), (255, 160, 80)]
        self.alpha = 72
        self.outline_alpha = 120

    @benchmark(lambda self: self.perf_log, 'VortexFieldRenderSystem.update')
    def update(self, world, screen: pygame.Surface, camera):
        pos_map = world.components.get('Position', {})
        fields = world.components.get('ForceFieldComponent', {})
        if not fields:
            return
        for eid, comp in list(fields.items()):
            pos = pos_map.get(eid)
            if pos is None:
                continue
            try:
                radius = float(getattr(comp, 'radius', 0.0))
                if radius <= 0:
                    continue
            except Exception:
                continue
            mode = (getattr(comp, 'mode', 'pull') or 'pull').lower()
            base_colors = self.colors_pull if mode != 'push' else self.colors_push
            color = base_colors[0]
            # Radio en píxeles con zoom
            rpx = int(radius * camera.zoom)
            if rpx <= 0:
                continue
            dim = rpx * 2
            # Superficie con alpha para el relleno suave
            surf = pygame.Surface((dim, dim), pygame.SRCALPHA)
            pygame.draw.circle(surf, (*color, self.alpha), (rpx, rpx), rpx)
            # Borde/outline para legibilidad
            pygame.draw.circle(surf, (*color, self.outline_alpha), (rpx, rpx), rpx, width=max(1, int(2 * camera.zoom)))
            # Posicionar centrado
            sx, sy = camera.apply((pos.x - radius, pos.y - radius))
            screen.blit(surf, (int(sx), int(sy)))
