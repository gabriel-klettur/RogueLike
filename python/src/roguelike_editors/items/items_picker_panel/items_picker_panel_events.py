import pygame

import logging
logger = logging.getLogger(__name__)

class ItemPickerPanelEventHandler:
    """
    Manejador de eventos para el editor de ítems.
    """
    def __init__(self, controller):
        self.controller = controller
        self.model = controller.model
        self.view = controller.view
        # Propiedades (texto/dc) ahora viven en el properties panel

    def handle(self, event: pygame.event.Event) -> None:
        # Teclas de toggle y navegación
        if event.type == pygame.KEYDOWN:
            if self.model.visible:
                # Delegar teclas al PickerPanel (mueve selección, Enter abre)
                self.controller.picker.handle_event(event, self.controller.picker_state)

        # Clicks del ratón
        elif event.type == pygame.MOUSEBUTTONDOWN and self.model.visible and event.button == 1:
            mx, my = event.pos
            logger.debug(f"[DEBUG items_picker] MOUSEBUTTONDOWN clicks={getattr(event, 'clicks',1)} pos=({mx},{my})")
            # Delegar click al PickerPanel
            self.controller.picker.handle_event(event, self.controller.picker_state)
            # Si el click fue fuera del panel y del info panel, limpiar selección
            if not self.controller.picker_state.rect.collidepoint(mx, my):
                self.model.selected_item_id = None

        elif event.type == pygame.MOUSEMOTION and self.model.visible:
            # Delegar hover/drag al PickerPanel
            self.controller.picker.handle_event(event, self.controller.picker_state)

        elif self.model.visible and event.type in (pygame.MOUSEBUTTONUP, pygame.MOUSEWHEEL):
            # Scroll de rueda y fin de click al PickerPanel
            self.controller.picker.handle_event(event, self.controller.picker_state)

        else:
            # No-op para otros eventos
            pass