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
            if event.key == pygame.K_F7:
                self.model.visible = not self.model.visible
                logger.debug(f"[DEBUG ItemEditorController] F7 pressed, visible={self.model.visible}")
                if not self.model.visible:
                    self.model.selected_item_id = None
            elif self.model.visible:
                # Delegar teclas al PickerPanel (mueve selección, Enter abre)
                self.controller.picker.handle_event(event, self.controller.picker_state)

        # Clicks del ratón
        elif event.type == pygame.MOUSEBUTTONDOWN and self.model.visible and event.button == 1:
            mx, my = event.pos
            logger.debug(f"[DEBUG items_picker] MOUSEBUTTONDOWN clicks={getattr(event, 'clicks',1)} pos=({mx},{my})")
            # Si clic en el panel de propiedades, no tocar selección del grid
            prop_rect = getattr(self.controller.properties_panel.model, 'panel_rect', None)
            if prop_rect and prop_rect.collidepoint(mx, my):
                return
            # Delegar click al PickerPanel
            self.controller.picker.handle_event(event, self.controller.picker_state)
            # Si el click fue fuera del panel y del info panel, limpiar selección
            if not self.controller.picker_state.rect.collidepoint(mx, my):
                if not prop_rect or not prop_rect.collidepoint(mx, my):
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