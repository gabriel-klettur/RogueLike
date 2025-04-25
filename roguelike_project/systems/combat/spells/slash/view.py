import pygame
import math

class SlashView:
    def __init__(self, model):
        self.model = model

    def render(self, screen, camera):
        cx = self.model.x
        cy = self.model.y
        dirty_rect = None

        for p in self.model.particles:
            if p["age"] >= p["lifespan"]:
                continue

            x = cx + p["offset_x"] + math.cos(p["angle"]) * p["speed"] * p["age"]
            y = cy + p["offset_y"] + math.sin(p["angle"]) * p["speed"] * p["age"]

            alpha = max(0, 255 * (1 - p["age"] / p["lifespan"]))
            surf = pygame.Surface((p["size"], p["size"]), pygame.SRCALPHA)
            surf.fill((*p["color"], int(alpha)))
            pos = camera.apply((x, y))
            rect = screen.blit(surf, pos)

            if dirty_rect:
                dirty_rect.union_ip(rect)
            else:
                dirty_rect = rect.copy()

        return dirty_rect
