import pygame
import roguelike_engine.config.config as config
from roguelike_engine.utils.benchmark import benchmark


class PatrolDebugSystem:
    """
    Visualiza la información de patrulla de NPCs cuando DEBUG=True:
    - Para patrones con waypoints: puntos de ruta y líneas, índice actual.
    - Textos con estado (waiting/dwell, distancia restante, patrón).
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        self._font = None
        self._circle_cache = {}  # radius_px -> Surface

    def _get_font(self, zoom: float):
        size = max(10, int(12 * zoom))
        if self._font is None or self._font.get_height() != size:
            self._font = pygame.font.SysFont("Arial", size)
        return self._font

    def _circle_surface(self, radius: int, color=(255, 255, 0)):
        if radius <= 0:
            radius = 1
        key = (radius, color)
        surf = self._circle_cache.get(key)
        if surf is None:
            size = radius * 2 + 2
            surf = pygame.Surface((size, size), flags=pygame.SRCALPHA)
            pygame.draw.circle(surf, color, (radius + 1, radius + 1), radius, 1)
            self._circle_cache[key] = surf
        return surf
    
    def update(self, world, screen, camera):
        if not getattr(config, "DEBUG", False):
            return
        comps = world.components
        pos_store = comps.get('Position', {})
        route_store = comps.get('PatrolRoute', {})
        npc_store = comps.get('NPCState', {})

        view_rect = pygame.Rect(0, 0, camera.screen_width, camera.screen_height)
        zoom = camera.zoom
        font = self._get_font(zoom)

        for eid, route in route_store.items():
            pos = pos_store.get(eid)
            if pos is None:
                continue
            # World to screen helpers
            def to_screen(x, y):
                sx, sy = camera.apply((x, y))
                return int(sx), int(sy)

            # Draw pattern info
            pattern = getattr(route, 'pattern_id', None)
            cx, cy = pos.x, pos.y

            # Try to inspect current FSM state for runtime data
            state_obj = None
            current_idx = None
            waiting = None
            dwell_timer = None
            try:
                fsm = npc_store.get(eid).fsm if npc_store.get(eid) else None
                state_obj = fsm.current_state if fsm else None
                current_idx = getattr(state_obj, 'current_index', None)
                waiting = getattr(state_obj, 'waiting', None)
                dwell_timer = getattr(state_obj, 'dwell_timer', None)
            except Exception:
                pass

            # Waypoint pattern: draw points and lines
            pts = getattr(route, 'points', None) or []
            n = len(pts)
            if n:
                # Draw polyline
                for i in range(n - 1):
                    x1, y1 = pts[i]
                    x2, y2 = pts[i + 1]
                    pygame.draw.line(screen, (0, 150, 255), to_screen(x1, y1), to_screen(x2, y2), max(1, int(2 * zoom)))
                # Close loop lightly
                if n > 2:
                    pygame.draw.line(screen, (0, 100, 200), to_screen(*pts[-1]), to_screen(*pts[0]), 1)
                # Draw points
                for i, (px, py) in enumerate(pts):
                    color = (255, 255, 255) if i != current_idx else (255, 0, 0)
                    pygame.draw.circle(screen, color, to_screen(px, py), max(2, int(3 * zoom)))
                # Label
                label = f"pattern={pattern or 'static'} idx={current_idx if current_idx is not None else '-'}"
                if waiting is True and dwell_timer is not None:
                    label += f" waiting {dwell_timer:.2f}s"
                text = font.render(label, True, (200, 200, 255))
                screen.blit(text, (to_screen(cx, cy)[0] + 8, to_screen(cx, cy)[1] - 8))
