import math
import pygame
from roguelike_game.ecs.utils.position_utils import compute_entity_center


class TelegraphRenderSystem:
    """Render TelegraphArc components as semi-transparent cones in world space."""
    def __init__(self, perf_log=None):
        self.perf_log = perf_log

    def update(self, world, screen, camera):
        arcs = world.components.get('TelegraphArc', {})
        if not arcs:
            return
        pos_map = world.components.get('Position', {})
        spr_map = world.components.get('Sprite', {})
        scl_map = world.components.get('Scale', {})
        for owner, arc in list(arcs.items()):
            pos = pos_map.get(owner)
            if pos is None:
                continue
            # Compute center using sprite+scale when available
            try:
                spr = spr_map.get(owner)
                scl = scl_map.get(owner)
                if spr:
                    c = compute_entity_center(pos, spr, scl)
                    cx, cy = float(c.x), float(c.y)
                else:
                    cx, cy = float(pos.x), float(pos.y)
            except Exception:
                cx, cy = float(pos.x), float(pos.y)
            r = float(getattr(arc, 'radius', 0.0) or 0.0)
            if r <= 0.0:
                continue
            arc_angle = float(getattr(arc, 'arc_angle', 0.0) or 0.0)
            if arc_angle <= 0.0:
                continue
            dx, dy = getattr(arc, 'direction', (1.0, 0.0))
            # Normalize direction defensively
            mag = (dx*dx + dy*dy) ** 0.5
            if mag > 1e-6:
                dx, dy = dx/mag, dy/mag
            # Apply offset along direction to position the arc center in front of the owner
            offset = float(getattr(arc, 'offset', 0.0) or 0.0)
            cx = cx + dx * offset
            cy = cy + dy * offset
            prog = float(getattr(arc, 'progress', 1.0) or 0.0)
            if prog < 0.0:
                prog = 0.0
            if prog > 1.0:
                prog = 1.0
            eff_r = r * prog
            if eff_r <= 0.0:
                continue
            left = cx - eff_r
            top = cy - eff_r
            w = int(eff_r * 2)
            h = int(eff_r * 2)
            if w <= 0 or h <= 0:
                continue
            overlay = pygame.Surface((w, h), pygame.SRCALPHA)
            dir_ang = math.atan2(dy, dx)
            start_ang = dir_ang - arc_angle / 2.0
            end_ang = dir_ang + arc_angle / 2.0
            pts = [(eff_r, eff_r)]
            segs = max(8, int(arc_angle / (2 * math.pi) * 24))
            for i in range(segs + 1):
                ang = start_ang + (end_ang - start_ang) * i / segs
                pts.append((eff_r + math.cos(ang) * eff_r, eff_r + math.sin(ang) * eff_r))
            color = getattr(arc, 'color', (255, 220, 0, 90))
            pygame.draw.polygon(overlay, color, pts)
            screen_left, screen_top = camera.apply((left, top))
            zoom = getattr(camera, 'zoom', 1.0) or 1.0
            if zoom != 1.0:
                zw, zh = int(w * zoom), int(h * zoom)
                if zw > 0 and zh > 0:
                    overlay = pygame.transform.scale(overlay, (zw, zh))
            screen.blit(overlay, (int(screen_left), int(screen_top)))
