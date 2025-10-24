import math
import pygame
import roguelike_engine.config.config as config
from roguelike_game.ecs.utils.collider_utils import build_collider_rect, get_circle_world
from roguelike_game.ecs.components.physics.circle_collider import CircleCollider
from roguelike_engine.utils.benchmark import benchmark


class HitboxDebugSystem:
    """
    Debug system to visualize HitboxComponent arcs and radii when DEBUG mode is enabled.
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log
        # cache fonts if needed
        self.font_cache = {}
        # cache circle surfaces by radius to avoid per-frame draw
        self.circle_surfs = {}
        # Reusable per-frame alpha overlay (screen-sized) to draw translucent shapes
        self._overlay = None
        self._overlay_size = (0, 0)
    
    def update(self, world, screen, camera):
        # view frustum culling
        view_rect = pygame.Rect(0, 0, camera.screen_width, camera.screen_height)
        # Ensure/reuse a screen-sized alpha surface for translucent draws
        sw, sh = screen.get_size()
        if self._overlay is None or self._overlay_size != (sw, sh):
            self._overlay = pygame.Surface((sw, sh), flags=pygame.SRCALPHA)
            self._overlay_size = (sw, sh)
        else:
            # Clear overlay (alpha=0)
            self._overlay.fill((0, 0, 0, 0))
        overlay = self._overlay
        # Siempre dibujar hitbox shapes cada frame en la superficie dada
        pos_store = world.components.get('Position', {})
        hb_store = world.components.get('HitboxComponent', {})
        for eid, hb in hb_store.items():
            pos = pos_store.get(eid)
            if pos is None:
                continue
            cx, cy = pos.x, pos.y
            r = hb.radius
            dir_x, dir_y = hb.direction
            ang_center = math.atan2(dir_y, dir_x)
            start_ang = ang_center - hb.arc_angle / 2
            end_ang = ang_center + hb.arc_angle / 2
            # Compute bounding rect of the circle in world coords
            left = cx - r
            top = cy - r
            # Convert to screen coords
            screen_left, screen_top = camera.apply((left, top))
            rect = pygame.Rect(int(screen_left), int(screen_top), int(r * 2), int(r * 2))
            # Skip drawing if off-screen (based on circle bounds)
            if not rect.colliderect(view_rect):
                continue
            # 1) Draw the exact sector polygon (filled) used by collision
            #    Build points in world space, then convert to screen space.
            segs = max(4, int(hb.arc_angle / (2 * math.pi) * 16))
            pts_world = [(cx, cy)]
            if segs <= 0:
                segs = 4
            for i in range(segs + 1):
                ang = start_ang + (end_ang - start_ang) * (i / segs)
                px = cx + math.cos(ang) * r
                py = cy + math.sin(ang) * r
                pts_world.append((px, py))
            # Convert to screen coordinates
            pts_screen = [camera.apply(p) for p in pts_world]
            # Draw filled sector with transparency onto the reusable overlay
            pygame.draw.polygon(overlay, (0, 255, 0, 64), [(int(x), int(y)) for (x, y) in pts_screen])
            # Outline the sector for clarity (on overlay so it's above the fill)
            pygame.draw.lines(overlay, (0, 200, 0), False, [(int(x), int(y)) for (x, y) in pts_screen[1:]], 2)
            # 2) Draw outer circle (radius) as thin red outline (cached)
            radius = int(r)
            if radius > 0:
                surf = self.circle_surfs.get(radius)
                if surf is None:
                    size = radius * 2 + 2
                    surf = pygame.Surface((size, size), flags=pygame.SRCALPHA)
                    pygame.draw.circle(surf, (255, 0, 0), (radius + 1, radius + 1), radius, 1)
                    self.circle_surfs[radius] = surf
                cx_scr, cy_scr = camera.apply((cx, cy))
                overlay.blit(surf, (int(cx_scr - (radius + 1)), int(cy_scr - (radius + 1))))
            # 3) Draw origin crosshair and direction arrow for orientation debugging
            cx_scr, cy_scr = camera.apply((cx, cy))
            pygame.draw.line(overlay, (255, 255, 0), (int(cx_scr) - 3, int(cy_scr)), (int(cx_scr) + 3, int(cy_scr)), 1)
            pygame.draw.line(overlay, (255, 255, 0), (int(cx_scr), int(cy_scr) - 3), (int(cx_scr), int(cy_scr) + 3), 1)
            tip_x = cx + dir_x * r
            tip_y = cy + dir_y * r
            tx, ty = camera.apply((tip_x, tip_y))
            pygame.draw.line(overlay, (255, 215, 0), (int(cx_scr), int(cy_scr)), (int(tx), int(ty)), 2)
        # Blit the accumulated translucent overlay once
        screen.blit(overlay, (0, 0))
        # Draw all colliders (bounding rect) in blue for debugging
        multi_store = world.components.get('MultiCollider', {})
        for tid, multi in multi_store.items():
            tpos = pos_store.get(tid)
            if tpos is None:
                continue
            for name, collider in multi.colliders.items():
                rect_w = build_collider_rect(tpos.x, tpos.y, collider)
                screen_x, screen_y = camera.apply((rect_w.x, rect_w.y))
                rect_s = pygame.Rect(int(screen_x), int(screen_y), int(rect_w.width), int(rect_w.height))
                # cull off-screen colliders
                if not rect_s.colliderect(view_rect):
                    continue
                pygame.draw.rect(screen, (0, 0, 255), rect_s, 1)
                # If it's a circle collider, draw its true circle (use cached surfaces)
                if hasattr(collider, "radius"):
                    cx, cy, r = get_circle_world(tpos.x, tpos.y, collider)
                    cx_s, cy_s = camera.apply((cx, cy))
                    radius = int(r)
                    if radius > 0:
                        surf = self.circle_surfs.get(radius)
                        if surf is None:
                            size = radius * 2 + 2
                            surf = pygame.Surface((size, size), flags=pygame.SRCALPHA)
                            # magenta outline for feet circles
                            pygame.draw.circle(surf, (255, 0, 255), (radius+1, radius+1), radius, 1)
                            self.circle_surfs[radius] = surf
                        # cull using the circle's bounding rect in screen space
                        circle_rect = pygame.Rect(int(cx_s - (radius+1)), int(cy_s - (radius+1)), int(radius*2+2), int(radius*2+2))
                        if circle_rect.colliderect(view_rect):
                            screen.blit(surf, (int(cx_s - (radius+1)), int(cy_s - (radius+1))))