import pygame
import math
from pygame import Rect
from roguelike_game.ecs.systems.rendering.combat.spells.sphere_magic_shield.model import SphereMagicShieldModel

class SphereMagicShieldView:
    """
    View for pulsing magic shield: renders a circle around (x,y).
    """
    def __init__(self, model: SphereMagicShieldModel):
        self.model = model

    def render(self, screen, camera):
        if self.model.is_finished():
            return None
        # World center and current radius
        px, py = self.model.x, self.model.y
        radius = max(4, int(self.model.radius))

        # Small offscreen surface just around the shield
        size = (radius * 2, radius * 2)
        surf = pygame.Surface(size, pygame.SRCALPHA)

        # Rotating dotted ring parameters (similar to Spells Picker ParticlePreviewAura)
        count = 24
        # Rotate a bit over time; keep subtle so it feels magical, not teleport-like
        theta = self.model.elapsed() * 1.6
        cx = cy = radius

        # Draw dotted ring
        base_color = self.model.color
        for i in range(count):
            ang = theta + (i / count) * (2 * math.pi)
            x = int(cx + radius * math.cos(ang))
            y = int(cy + radius * math.sin(ang))
            # Soft pulsating alpha per-dot
            a = 140 + int(100 * (0.5 + 0.5 * math.sin(ang * 2)))
            dot = pygame.Surface((3, 3), pygame.SRCALPHA)
            dot.fill((*base_color, max(0, min(255, a))))
            # Clamp inside local surface
            if 0 <= x < size[0] and 0 <= y < size[1]:
                surf.blit(dot, (x, y))

        # Optional faint inner ring for cohesion
        faint_alpha = 60
        pygame.draw.circle(surf, (*base_color, faint_alpha), (cx, cy), max(1, radius - 2), width=1)

        # Blit to screen at the correct world position
        world_tl = (px - radius, py - radius)
        screen_tl = camera.apply(world_tl)
        screen.blit(surf, screen_tl)
        return Rect(screen_tl, (size[0], size[1]))
