import pygame

from roguelike_editors.entities.entities_properties_panel.entities_type_assets.entities_type_assets_model import EntitiesTypeAssetsModel

class EntitiesTypeAssetsView:
    """View for drawing main 'properties'/'assets' tabs."""
    def __init__(self, font: pygame.font.Font):
        self.font = font

    def draw(self, screen: pygame.Surface, model: EntitiesTypeAssetsModel) -> None:
        """Draw main tabs at top of EntityPropertiesPanel."""
        font_h = self.font.get_height()
        padding_x, padding_y = 10, 5
        panel_rect = model.parent_model.panel_rect
        if not panel_rect:
            return
        x_cursor, y = panel_rect.x, panel_rect.y
        model.type_tab_rects.clear()
        mouse_pos = pygame.mouse.get_pos()

        for label in model.type_tabs:
            text_label = label.capitalize()
            text_w, text_h = self.font.size(text_label)
            w = text_w + padding_x * 2
            h = text_h + padding_y * 2
            rect = pygame.Rect(x_cursor, y, w, h)
            model.type_tab_rects[label] = rect
            is_active = (model.active_type_tab == label)
            is_hover = rect.collidepoint(mouse_pos)
            if is_active or is_hover:
                surf = pygame.Surface((w, h), pygame.SRCALPHA)
                surf.fill((255, 255, 0, 100))
                screen.blit(surf, (rect.x, rect.y))
                pygame.draw.rect(screen, (255, 255, 0), rect, 2)
            else:
                pygame.draw.rect(screen, (100, 100, 100), rect)
                pygame.draw.rect(screen, (255, 255, 255), rect, 2)
            text_surf = self.font.render(text_label, True, (0, 0, 0))
            text_x = x_cursor + (w - text_surf.get_width()) // 2
            text_y = y + padding_y
            screen.blit(text_surf, (text_x, text_y))
            x_cursor += w
