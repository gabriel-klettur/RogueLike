import pygame
from roguelike_editors.entities.entities_properties_panel.services.state_tabs_helpers import (
    hit_test_state_tab,
)

class EntitiesStateTabsEventHandler:
    """Manejador de eventos para las pestañas de estado de la entidad."""
    def __init__(self, controller):
        self.controller = controller
        self.model = controller.model
        self.parent = controller.parent_model

    def handle(self, event: pygame.event.Event) -> bool:
        """Detecta clics en las pestañas de estado."""
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
            # Solo si clic dentro del panel
            if not self.parent.panel_rect or not self.parent.panel_rect.collidepoint(event.pos):
                return False
            # Detectar pestaña bajo el cursor
            hit_label = hit_test_state_tab(self.model.state_tab_rects, event.pos)
            if hit_label is not None:
                # Actualizar pestaña activa
                self.model.active_state_tab = hit_label
                return True
        return False
