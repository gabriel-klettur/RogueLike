# File: roguelike_project/systems/combat/spells/healing_aura/view.py
import pygame
from .model import HealingAuraModel

class HealingAuraView:
    """
    Vista: renderiza el estado del modelo en pantalla.
    """
    def __init__(self, model: HealingAuraModel):
        self.model = model

    def render(self, screen, camera):
        m = self.model
        # Dibuja el óvalo base
        sprite_w = m.player.sprite_size[0]
        base_x = m.player.x + sprite_w / 2
        base_y = m.player.y + 96  # Altura de pies
        world_pos = camera.apply((base_x, base_y))

        ellipse_width, _ = camera.scale((sprite_w, 1))
        ellipse_height = int(ellipse_width * 0.3)
        alpha = max(0, 255 * (1 - m.timer / m.elipse_lifespan))

        oval_surface = pygame.Surface((ellipse_width, ellipse_height), pygame.SRCALPHA)
        pygame.draw.ellipse(
            oval_surface,
            (0, 255, 100, int(alpha)),
            (0, 0, ellipse_width, ellipse_height)
        )
        # Ajuste fino en Y para alinear el óvalo al suelo
        elipse_offset_y = 10
        screen.blit(
            oval_surface,
            (
                world_pos[0] - ellipse_width // 2,
                world_pos[1] - ellipse_height // 4 + elipse_offset_y
            )
        )

        # Dibuja partículas individuales
        for p in m.particles:
            pos = p.get_world_pos()
            screen_pos = camera.apply((pos.x, pos.y))
            # Transparencia decreciente
            alpha_p = max(0, 255 * (1 - p.age / p.lifespan))
            size = p.size
            surf = pygame.Surface((size, size), pygame.SRCALPHA)
            surf.fill((*p.color, int(alpha_p)))
            screen.blit(surf, screen_pos)
