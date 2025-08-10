import pygame
from roguelike_editors.entities.entities_properties_panel.services.assets_constants import (
    SUBTAB_SET,
    SUBTAB_NO_SET,
)


class EntitiesAssetsSubTabsView:
    """View for drawing the Assets sub-tabs (Set / No-Set)."""

    def __init__(self, font: pygame.font.Font):
        self.font = font

    def draw(self, screen: pygame.Surface, model, panel_rect: pygame.Rect) -> None:
        """Draw the asset sub-tabs below the state tabs."""
        font_h = self.font.get_height()
        padding_x, padding_y = 8, 4
        x_cursor = panel_rect.x
        # y-coordinate below state tabs (two header heights)
        header_h = font_h + padding_y * 2
        y = panel_rect.y + header_h * 2
        mouse_pos = pygame.mouse.get_pos()

        model.sub_tab_rects.clear()

        for label in model.sub_tabs:
            text_label = label.title()
            text_w, text_h = self.font.size(text_label)
            w = text_w + padding_x * 2
            h = text_h + padding_y * 2
            rect = pygame.Rect(x_cursor, y, w, h)
            model.sub_tab_rects[label] = rect

            is_active = (model.active_sub_tab == label)
            is_hover = rect.collidepoint(mouse_pos)

            if is_active or is_hover:
                surf = pygame.Surface((w, h), pygame.SRCALPHA)
                surf.fill((255, 255, 0, 80))
                screen.blit(surf, (rect.x, rect.y))
                pygame.draw.rect(screen, (255, 255, 0), rect, 2)
            else:
                pygame.draw.rect(screen, (80, 80, 80), rect)
                pygame.draw.rect(screen, (200, 200, 200), rect, 1)

            text_surf = self.font.render(text_label, True, (0, 0, 0))
            text_x = x_cursor + (w - text_surf.get_width()) // 2
            text_y = y + padding_y
            screen.blit(text_surf, (text_x, text_y))

            x_cursor += w
