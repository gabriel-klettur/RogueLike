import time
import pygame
from roguelike_engine.utils.benchmark import benchmark

class AuraRenderSystem:
    """
    Dibuja el óvalo base de las auras en los pies del caster.
    """
    def __init__(self, perf_log):
        self.perf_log = perf_log

    @benchmark(lambda self: self.perf_log, "AuraRenderSystem.update")
    def update(self, world, screen, camera):
        now = time.time()
        for eid, aura in world.components.get('AuraComponent', {}).items():
            pos_cmp = world.components['Position'][eid]
            x0, y0 = pos_cmp.x, pos_cmp.y
            sprite_cmp = world.components['Sprite'].get(eid)
            if sprite_cmp:
                sprite_w = sprite_cmp.image.get_width()
                base_x = x0 + sprite_w/2
                base_y = y0 + 96
            else:
                base_x, base_y = x0, y0
            world_pos = camera.apply((base_x, base_y))
            ellipse_width, _ = camera.scale((sprite_w if sprite_cmp else 32, 1))
            ellipse_height = int(ellipse_width * 0.3)
            alpha = max(0, 255 * (1 - (now - aura.start_time) / aura.duration))
            oval_surf = pygame.Surface((ellipse_width, ellipse_height), pygame.SRCALPHA)
            pygame.draw.ellipse(
                oval_surf,
                (0, 255, 100, int(alpha)),
                (0, 0, ellipse_width, ellipse_height)
            )
            el_y = 10
            screen.blit(
                oval_surf,
                (
                    world_pos[0] - ellipse_width // 2,
                    world_pos[1] - ellipse_height // 4 + el_y
                )
            )
