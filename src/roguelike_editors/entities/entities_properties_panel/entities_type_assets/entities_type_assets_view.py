import pygame

from roguelike_editors.entities.entities_properties_panel.entities_type_assets.entities_type_assets_model import EntitiesTypeAssetsModel
from roguelike_editors.entities.entities_properties_panel.services.state_tabs_helpers import (
    build_tab_rects,
    format_tab_label,
)

class EntitiesTypeAssetsView:
    """View for drawing main 'properties'/'assets' tabs."""
    def __init__(self, font: pygame.font.Font):
        self.font = font

    def draw(self, screen: pygame.Surface, model: EntitiesTypeAssetsModel) -> None:
        """Draw main tabs at top of EntityPropertiesPanel."""
        padding_x, padding_y = 10, 5
        panel_rect = model.parent_model.panel_rect
        if not panel_rect:
            return
        # Compute rectangles for tabs and store in model
        model.type_tab_rects = build_tab_rects(
            model.type_tabs, self.font, (panel_rect.x, panel_rect.y), (padding_x, padding_y)
        )
        mouse_pos = pygame.mouse.get_pos()

        for label, rect in model.type_tab_rects.items():
            is_active = (model.active_type_tab == label)
            is_hover = rect.collidepoint(mouse_pos)
            if is_active or is_hover:
                surf = pygame.Surface((rect.w, rect.h), pygame.SRCALPHA)
                surf.fill((255, 255, 0, 100))
                screen.blit(surf, (rect.x, rect.y))
                pygame.draw.rect(screen, (255, 255, 0), rect, 2)
            else:
                pygame.draw.rect(screen, (100, 100, 100), rect)
                pygame.draw.rect(screen, (255, 255, 255), rect, 2)
            text_label = format_tab_label(label)
            text_surf = self.font.render(text_label, True, (0, 0, 0))
            text_x = rect.x + (rect.w - text_surf.get_width()) // 2
            text_y = rect.y + padding_y
            screen.blit(text_surf, (text_x, text_y))
