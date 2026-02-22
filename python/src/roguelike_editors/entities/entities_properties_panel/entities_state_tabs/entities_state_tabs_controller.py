import pygame
from .entities_state_tabs_model import EntitiesStateTabsModel
from .entities_state_tabs_view import EntitiesStateTabsView
from .entities_state_tabs_events import EntitiesStateTabsEventHandler

class EntitiesStateTabsController:
    """Controller para las pestañas de estado de la entidad."""
    def __init__(self, parent_model, font: pygame.font.Font):
        # Modelo principal para sincronizar asset_tab
        self.parent_model = parent_model
        self.model = EntitiesStateTabsModel()
        self.view = EntitiesStateTabsView(font)
        self.event_handler = EntitiesStateTabsEventHandler(self)

    def draw(self, screen: pygame.Surface) -> None:
        """Dibuja las pestañas de estado en el panel de propiedades."""
        panel_rect = self.parent_model.panel_rect
        if not panel_rect:
            return
        self.view.draw(screen, self.model, panel_rect)

    def handle_event(self, event: pygame.event.Event) -> bool:
        """Procesa eventos para las pestañas de estado."""
        return self.event_handler.handle(event)
