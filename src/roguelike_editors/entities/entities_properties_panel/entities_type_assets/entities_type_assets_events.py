import pygame

from roguelike_editors.entities.entities_properties_panel.entities_type_assets.entities_type_assets_model import EntitiesTypeAssetsModel
from roguelike_editors.entities.entities_properties_panel.services.state_tabs_helpers import hit_test_state_tab

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
            label = hit_test_state_tab(self.model.type_tab_rects, event.pos)
            if label is None:
                return False
            # Change main tab and clear parent transient UI state
            self.model.active_type_tab = label
            self.controller.reset_parent_ui_state()
            return True
        return False
