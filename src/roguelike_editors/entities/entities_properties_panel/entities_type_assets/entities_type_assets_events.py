import pygame

from roguelike_editors.entities.entities_properties_panel.entities_type_assets.entities_type_assets_model import EntitiesTypeAssetsModel

class EntitiesTypeAssetsEventHandler:
    """Handles clicks on main 'properties'/'assets' tabs."""
    def __init__(self, controller):
        self.controller = controller
        self.model: EntitiesTypeAssetsModel = controller.model

    def handle_event(self, event: pygame.event.Event) -> bool:
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
            panel_rect = self.model.parent_model.panel_rect
            if not panel_rect or not panel_rect.collidepoint(event.pos):
                return False
            mx, my = event.pos
            for label, rect in self.model.type_tab_rects.items():
                if rect.collidepoint(mx, my):
                    # Cambiar pestaña principal
                    self.model.active_type_tab = label
                    # Reset parent model state
                    pm = self.model.parent_model
                    pm.focused_property = None
                    pm.editing_property = None
                    pm.hovered_property = None
                    return True
        return False
