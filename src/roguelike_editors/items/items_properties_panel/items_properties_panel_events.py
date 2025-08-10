import pygame
import logging

logger = logging.getLogger(__name__)


class ItemsPropertiesPanelEventHandler:
    """Manejador de eventos para el panel de propiedades de ítems."""

    def __init__(self, controller):
        self.controller = controller
        self.model = controller.model
        self.view = controller.view
        self.text_input = controller.text_input
        self.dc_detector = controller.dc_detector

    def handle(self, event: pygame.event.Event) -> None:
        # Entrada de texto para edición inline
        if self.text_input.active:
            if self.text_input.handle_event(event):
                self.model.editing_text = self.text_input.text
                self.model.editing_cursor = self.text_input.cursor
                if not self.text_input.active:
                    self.controller.commit_edit()
                return
            return

        # Clicks del ratón sobre propiedades
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
            mx, my = event.pos
            # Si clic en alguna propiedad
            for rect, key in getattr(self.model, 'property_entries', []):
                if rect.collidepoint(mx, my):
                    if getattr(event, 'clicks', 1) >= 2 or self.dc_detector.is_double_click(key):
                        self.model.focused_property = key
                        self.model.editing_property = key
                        # Cargar valor inicial desde ítem activo
                        active_id = self.controller._selected_id or self.controller._hovered_id
                        item = self.controller._items.get(active_id)
                        initial = str(getattr(item, key, "")) if item else ""
                        self.model.editing_text = initial
                        self.model.editing_cursor = len(initial)
                        self.text_input.activate(initial)
                    else:
                        self.model.focused_property = key
                    return

            # Clic fuera del panel: limpiar foco/edición
            panel = getattr(self.model, 'panel_rect', None)
            if panel and not panel.collidepoint(mx, my):
                self.model.focused_property = None
                self.model.editing_property = None
                return

        # No-op para otros eventos (tooltips se dibujan en la vista)
        return
