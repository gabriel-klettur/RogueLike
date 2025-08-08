import pygame
from roguelike_editors.entities.entities_properties_panel.services.assets_constants import (
    SUBTAB_SET,
    SUBTAB_NO_SET,
)


class EntitiesAssetsSubTabsEventHandler:
    """Event handler for the Assets sub-tabs (Set / No-Set)."""

    def __init__(self, controller):
        self.controller = controller
        self.model = controller.model
        self.parent = controller.parent_model

    def handle(self, event: pygame.event.Event) -> bool:
        """Handle click events for asset subtabs."""
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
            if not self.parent.panel_rect or not self.parent.panel_rect.collidepoint(event.pos):
                return False
            mx, my = event.pos
            for label, rect in self.model.sub_tab_rects.items():
                if rect.collidepoint(mx, my):
                    self.model.active_sub_tab = label
                    return True
        return False
