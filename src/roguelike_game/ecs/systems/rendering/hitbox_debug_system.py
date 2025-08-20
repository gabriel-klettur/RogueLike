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

    @benchmark(lambda self: self.perf_log, "4.2.2.HitboxDebugSystem.update")
    def update(self, world, screen, camera):
        # view frustum culling
        view_rect = pygame.Rect(0, 0, camera.screen_width, camera.screen_height)
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
            # Skip drawing if off-screen
            if not rect.colliderect(view_rect):
                continue
            # Blit cached circle surface instead of drawing each frame
            radius = int(r)
            if radius > 0:
                surf = self.circle_surfs.get(radius)
                if surf is None:
                    size = radius * 2 + 2
                    surf = pygame.Surface((size, size), flags=pygame.SRCALPHA)
                    pygame.draw.circle(surf, (255, 0, 0), (radius+1, radius+1), radius, 1)
                    self.circle_surfs[radius] = surf
                cx_scr, cy_scr = camera.apply((cx, cy))
                screen.blit(surf, (int(cx_scr - (radius+1)), int(cy_scr - (radius+1))))
            # Draw arc segment in green
            pygame.draw.arc(screen, (0, 255, 0), rect, start_ang, end_ang, 2)
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
                if isinstance(collider, CircleCollider):
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