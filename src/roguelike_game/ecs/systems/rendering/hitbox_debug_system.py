import math
import pygame
import roguelike_engine.config.config as config
from roguelike_game.ecs.utils.collider_utils import build_collider_rect
from roguelike_engine.utils.benchmark import benchmark


class HitboxDebugSystem:
    """
    Debug system to visualize HitboxComponent arcs and radii when DEBUG mode is enabled.
    """
    def __init__(self, perf_log=None):
        self.perf_log = perf_log

    @benchmark(lambda self: self.perf_log, "4.2.2.HitboxDebugSystem.update")
    def update(self, world, screen, camera):
        # Only render if hitbox debug is active
        if not config.DEBUG_HITBOX:
            return
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
            # Draw full circle outline in red
            center_screen = camera.apply((cx, cy))
            pygame.draw.circle(screen, (255, 0, 0), (int(center_screen[0]), int(center_screen[1])), int(r), 1)
            # Draw arc segment in green
            pygame.draw.arc(screen, (0, 255, 0), rect, start_ang, end_ang, 2)
        # Draw all colliders (bounding rect) in blue for debugging
        multi_store = world.components.get('MultiCollider', {})
        for tid, multi in multi_store.items():
            tpos = pos_store.get(tid)
            if tpos is None:
                continue
            for collider in multi.colliders.values():
                rect_w = build_collider_rect(tpos.x, tpos.y, collider)
                screen_x, screen_y = camera.apply((rect_w.x, rect_w.y))
                rect_s = pygame.Rect(int(screen_x), int(screen_y), int(rect_w.width), int(rect_w.height))
                pygame.draw.rect(screen, (0, 0, 255), rect_s, 1)
