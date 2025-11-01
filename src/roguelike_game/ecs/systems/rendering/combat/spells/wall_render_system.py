import pygame
from roguelike_engine.utils.benchmark import benchmark


class WallRenderSystem:
    """
    Renderiza segmentos de muro como rectángulos orientados (OBB) translúcidos.
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log

    @benchmark(lambda self: self.perf_log, 'WallRenderSystem.update')
    def update(self, world, screen: pygame.Surface, camera):
        pos_map = world.components.get('Position', {})
        walls = world.components.get('WallSegmentComponent', {})
        if not walls:
            return
        for eid, comp in list(walls.items()):
            pos = pos_map.get(eid)
            if pos is None:
                continue
            half_w = float(getattr(comp, 'half_w', getattr(comp, 'width', 0.0) * 0.5) or 0.0)
            half_h = float(getattr(comp, 'half_h', getattr(comp, 'height', 0.0) * 0.5) or 0.0)
            if half_w <= 0 or half_h <= 0:
                continue
            # Color azul translúcido
            color = (120, 180, 255)
            alpha = 140
            # Calcular vértices del OBB en mundo
            cos_a = float(getattr(comp, 'cos_a', 1.0))
            sin_a = float(getattr(comp, 'sin_a', 0.0))
            # Local corners
            corners = [
                (-half_w, -half_h),
                ( half_w, -half_h),
                ( half_w,  half_h),
                (-half_w,  half_h),
            ]
            world_pts = []
            for (lx, ly) in corners:
                wx = pos.x + lx * cos_a - ly * sin_a
                wy = pos.y + lx * sin_a + ly * cos_a
                sx, sy = camera.apply((wx, wy))
                world_pts.append((int(sx), int(sy)))
            # Dibujar polígono con alpha: usar surface temporal para alpha
            # Bounding rect de pantalla
            min_x = min(p[0] for p in world_pts)
            min_y = min(p[1] for p in world_pts)
            max_x = max(p[0] for p in world_pts)
            max_y = max(p[1] for p in world_pts)
            bw = max_x - min_x + 2
            bh = max_y - min_y + 2
            if bw <= 0 or bh <= 0:
                continue
            temp = pygame.Surface((bw, bh), pygame.SRCALPHA)
            shifted = [(p[0] - min_x, p[1] - min_y) for p in world_pts]
            pygame.draw.polygon(temp, (*color, alpha), shifted)
            screen.blit(temp, (min_x, min_y))
