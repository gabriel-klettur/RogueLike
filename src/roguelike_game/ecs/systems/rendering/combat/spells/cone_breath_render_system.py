import math
import pygame
from roguelike_engine.utils.benchmark import benchmark
from roguelike_game.ecs.systems.combat.spells.resolvers_pkg.utils import get_entity_center, mouse_world


class ConeBreathRenderSystem:
    """
    Dibuja un cono translúcido para los ConeBreathComponent activos.
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        # Color fuego semi-transparente
        self.fill_color = (255, 120, 60, 128)
        self.edge_color = (255, 160, 90, 200)
        # Segmentos para aproximar el arco
        self.min_segments = 12

    @benchmark(lambda self: self.perf_log, 'ConeBreathRenderSystem.update')
    def update(self, world, screen: pygame.Surface, camera):
        comps = world.components.get('ConeBreathComponent', {})
        if not comps:
            return
        for eid, comp in list(comps.items()):
            try:
                owner = getattr(comp, 'owner', None)
                if owner is None:
                    continue
                # Centro del caster y dirección
                cx, cy = get_entity_center(world, int(owner))
                arc_deg = float(getattr(comp, 'arc_degrees', 0.0) or 0.0)
                arc_rad = math.radians(max(1.0, arc_deg))
                length = float(getattr(comp, 'length', 0.0) or 0.0)
                offset = float(getattr(comp, 'offset', 0.0) or 0.0)
                # Dirección: usar initial_direction si está, si no, mouse
                dir_xy = getattr(comp, 'initial_direction', None)
                if isinstance(dir_xy, (list, tuple)) and len(dir_xy) >= 2:
                    dx, dy = float(dir_xy[0]), float(dir_xy[1])
                    mag = max(1e-6, (dx*dx + dy*dy) ** 0.5)
                    dir_x, dir_y = dx / mag, dy / mag
                else:
                    if camera is not None:
                        wx, wy = mouse_world(camera)
                    else:
                        wx, wy = cx, cy
                    dx, dy = wx - cx, wy - cy
                    mag = max(1e-6, (dx*dx + dy*dy) ** 0.5)
                    dir_x, dir_y = dx / mag, dy / mag
                # Centro del cono: aplicar offset
                base_x = cx + dir_x * offset
                base_y = cy + dir_y * offset
                # Polígono aproximado del sector
                center_ang = math.atan2(dir_y, dir_x)
                start_ang = center_ang - arc_rad / 2.0
                end_ang = center_ang + arc_rad / 2.0
                segs = max(self.min_segments, int(arc_rad / (2 * math.pi) * 64))
                pts = []
                # Convertir coords mundo->pantalla primero para el centro
                if camera is not None:
                    sx, sy = camera.apply((base_x, base_y))
                    zoom = float(getattr(camera, 'zoom', 1.0) or 1.0)
                else:
                    sx, sy = (base_x, base_y)
                    zoom = 1.0
                # Crear una superficie temporal para alpha
                # Bounding box aproximada del sector
                r = max(2, int(length * zoom))
                box = pygame.Rect(int(sx - r), int(sy - r), int(2 * r), int(2 * r))
                if box.width <= 2 or box.height <= 2:
                    continue
                surf = pygame.Surface((box.width, box.height), pygame.SRCALPHA)
                # Puntos del polígono en coordenadas locales a 'surf'
                cx_local, cy_local = r, r
                pts.append((cx_local, cy_local))
                for i in range(segs + 1):
                    ang = start_ang + (end_ang - start_ang) * (i / segs)
                    px = cx_local + math.cos(ang) * length * zoom
                    py = cy_local + math.sin(ang) * length * zoom
                    pts.append((px, py))
                # Relleno y contorno
                pygame.draw.polygon(surf, self.fill_color, pts)
                try:
                    pygame.draw.lines(surf, self.edge_color, False, pts[1:], max(2, int(2*zoom)))
                except Exception:
                    pass
                # Blit al screen
                screen.blit(surf, box.topleft)
            except Exception:
                continue
