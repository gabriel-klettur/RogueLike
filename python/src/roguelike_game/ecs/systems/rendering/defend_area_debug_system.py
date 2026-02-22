import pygame
import roguelike_engine.config.config as config
from roguelike_engine.utils.benchmark.benchmark import benchmark


class DefendAreaDebugSystem:
    """
    Renders DefendArea areas (circle or square) and optional labels/links for NPCs when DEBUG=True.

    Visual style:
    - Orange translucent filled circle or square with solid outline at defend center.
    - Thin line from NPC to defend center for quick association.
    - Compact label with shape and radius in pixels.
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        self._font = None
        # Cache circle surfaces per (radius_px_screen, color)
        self._circle_cache = {}
        # Cache square surfaces per (half_side_px_screen, color)
        self._square_cache = {}

    def _get_font(self, zoom: float):
        size = max(10, int(12 * zoom))
        if self._font is None or self._font.get_height() != size:
            self._font = pygame.font.SysFont("Arial", size)
        return self._font

    def _circle_surface(self, radius: int, fill_rgba=(255, 165, 0, 40), outline_rgb=(255, 165, 0)):
        # Build cached surface with alpha fill and outline
        if radius <= 0:
            radius = 1
        key = (radius, fill_rgba, outline_rgb)
        surf = self._circle_cache.get(key)
        if surf is None:
            size = radius * 2
            surf = pygame.Surface((size, size), flags=pygame.SRCALPHA)
            pygame.draw.circle(surf, fill_rgba, (radius, radius), radius, width=0)
            pygame.draw.circle(surf, outline_rgb, (radius, radius), radius, width=2)
            self._circle_cache[key] = surf
        return surf

    def _square_surface(self, half_side: int, fill_rgba=(255, 165, 0, 40), outline_rgb=(255, 165, 0)):
        # Build cached square surface centered (blit offset handled by caller)
        if half_side <= 0:
            half_side = 1
        key = (half_side, fill_rgba, outline_rgb)
        surf = self._square_cache.get(key)
        if surf is None:
            size = half_side * 2
            surf = pygame.Surface((size, size), flags=pygame.SRCALPHA)
            rect = pygame.Rect(0, 0, size, size)
            surf.fill((0, 0, 0, 0))
            pygame.draw.rect(surf, fill_rgba, rect, width=0)
            pygame.draw.rect(surf, outline_rgb, rect, width=2)
            self._square_cache[key] = surf
        return surf
    
    def update(self, world, screen, camera):
        if not getattr(config, "DEBUG", False):
            return
        comps = world.components
        defend_store = comps.get('DefendArea', {})
        pos_store = comps.get('Position', {})
        if not defend_store:
            return
        zoom = getattr(camera, 'zoom', 1.0) or 1.0
        font = self._get_font(zoom)

        # Iterate all entities with DefendArea
        for eid, defend in defend_store.items():
            cx_w = getattr(defend, 'center_x', None)
            cy_w = getattr(defend, 'center_y', None)
            r_w = getattr(defend, 'radius_px', None)
            if cx_w is None or cy_w is None or r_w is None:
                continue
            # World->screen
            sx, sy = camera.apply((cx_w, cy_w))
            sx = int(sx); sy = int(sy)
            sr = max(1, int(r_w * zoom))
            shape = str(getattr(defend, 'shape', 'circle') or 'circle').lower()

            # Overlay at defend center depending on shape
            if shape == 'square':
                square = self._square_surface(sr)
                screen.blit(square, (sx - sr, sy - sr))
            else:
                circle = self._circle_surface(sr)
                screen.blit(circle, (sx - sr, sy - sr))

            # Draw link from NPC to center if we have Position
            pos = pos_store.get(eid)
            if pos is not None:
                px, py = camera.apply((pos.x, pos.y))
                pygame.draw.line(screen, (255, 180, 80), (int(px), int(py)), (sx, sy), max(1, int(1 * zoom)))

            # Label: radius in px and shape
            try:
                label = f"defend {shape} r={int(r_w)}px"
                text = font.render(label, True, (255, 200, 120))
                screen.blit(text, (sx + 8, sy - 8))
            except Exception:
                pass
