import pygame
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
        px, py = self.model.x, self.model.y
        radius = self.model.radius
        # Create surface
        surf = pygame.Surface((radius*2, radius*2), pygame.SRCALPHA)
        alpha = int(150 * (1 - self.model.elapsed() / self.model.duration))
        pygame.draw.circle(surf, (*self.model.color, alpha), (radius, radius), radius, width=4)
        # Blit
        world_tl = (px - radius, py - radius)
        screen_tl = camera.apply(world_tl)
        screen.blit(surf, screen_tl)
        return Rect(screen_tl, (radius*2, radius*2))
