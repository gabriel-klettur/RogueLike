import pygame
from .entities_assets_subtabs_model import EntitiesAssetsSubTabsModel
from .entities_assets_subtabs_view import EntitiesAssetsSubTabsView
from .entities_assets_subtabs_events import EntitiesAssetsSubTabsEventHandler


class EntitiesAssetsSubTabsController:
    """Controller for the Assets sub-tabs (Set / No-Set)."""

    def __init__(self, parent_model, font: pygame.font.Font):
        self.parent_model = parent_model
        self.model = EntitiesAssetsSubTabsModel()
        self.view = EntitiesAssetsSubTabsView(font)
        self.event_handler = EntitiesAssetsSubTabsEventHandler(self)

    def draw(self, screen: pygame.Surface) -> None:
        """Draw the asset sub-tabs under the state tabs."""
        panel_rect = self.parent_model.panel_rect
        if not panel_rect:
            return
        self.view.draw(screen, self.model, panel_rect)

    def handle_event(self, event: pygame.event.Event) -> bool:
        """Handle click events on the asset sub-tabs."""
        return self.event_handler.handle(event)
