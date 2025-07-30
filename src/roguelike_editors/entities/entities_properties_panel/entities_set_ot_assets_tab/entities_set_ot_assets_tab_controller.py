import pygame
from .entities_set_ot_assets_tab_model import EntitiesSetOtAssetsTabModel
from .entities_set_ot_assets_tab_view import EntitiesSetOtAssetsTabView
from .entities_set_ot_assets_tab_events import EntitiesSetOtAssetsTabEventHandler

class EntitiesSetOtAssetsTabController:
    """Controller for the 'Asset Set' and 'Asset by Asset' subtabs."""
    def __init__(self, parent_model, font: pygame.font.Font):
        self.parent_model = parent_model
        self.model = EntitiesSetOtAssetsTabModel()
        self.view = EntitiesSetOtAssetsTabView(font)
        self.event_handler = EntitiesSetOtAssetsTabEventHandler(self)

    def draw(self, screen: pygame.Surface) -> None:
        """Draw the asset sub-tabs under the state tabs."""
        panel_rect = self.parent_model.panel_rect
        if not panel_rect:
            return
        self.view.draw(screen, self.model, panel_rect)

    def handle_event(self, event: pygame.event.Event) -> bool:
        """Handle click events on the asset sub-tabs."""
        return self.event_handler.handle(event)
