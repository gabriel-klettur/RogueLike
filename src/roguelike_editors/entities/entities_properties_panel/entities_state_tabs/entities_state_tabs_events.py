import pygame

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
            mx, my = event.pos
            for label, rect in self.model.state_tab_rects.items():
                if rect.collidepoint(mx, my):
                    # Actualizar pestaña activa
                    self.model.active_state_tab = label
                    
                    return True
        return False
