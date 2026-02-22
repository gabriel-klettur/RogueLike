import pygame
import time
from pygame import Surface
from roguelike_game.ecs.systems.rendering.combat.spells.teleport.model import TeleportModel

class TeleportView:
    """
    ECS view for teleport effect: draws expanding circle and optional fade.
    """
    def __init__(self, model: TeleportModel):
        self.model = model

    def render(self, screen, camera):
        # nothing to do if finished
        if self.model.is_finished():
            return

        elapsed = time.time() - self.model.start_time
        total = self.model.lifespan
        t = min(1.0, elapsed / total)

        max_radius = 60
        radius = int(max_radius * t)

        alpha = int(255 * (1 - t))
        surf = Surface(screen.get_size(), pygame.SRCALPHA)
        col = (0, 200, 255, alpha)

        # choose center based on phase
        center_world = self.model.start_pos if self.model.phase == 'out' else self.model.end_pos
        center_px = camera.apply(center_world)

        pygame.draw.circle(surf, col, center_px, radius, width=4)
        screen.blit(surf, (0, 0))
