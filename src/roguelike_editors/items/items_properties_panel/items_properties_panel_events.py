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
            # Si el TextInput no consumió el evento, dejamos continuar (para permitir scroll, etc.)

        # Scroll con rueda del ratón cuando el cursor está sobre el panel
        if event.type == pygame.MOUSEWHEEL:
            panel = getattr(self.model, 'panel_rect', None)
            if panel:
                mx, my = pygame.mouse.get_pos()
                if panel.collidepoint(mx, my):
                    content_h = getattr(self.model, 'content_height', 0)
                    # Viewport alto basado en padding fijo de la vista (10px por lado)
                    view_h = max(0, panel.h - 20)
                    # Si aún no conocemos el alto de contenido (antes del primer draw), no reseteamos;
                    # dejamos acumular scroll y se normalizará tras el draw.
                    if content_h > 0 and content_h <= view_h:
                        logger.debug(f"[ItemsPropertiesPanel] wheel ignored (no overflow) content_h={content_h} view_h={view_h}")
                        self.model.scroll_y = 0
                        return
                    max_scroll = max(0, content_h - view_h) if content_h > 0 else None
                    # Sensibilidad: media altura de línea por tick
                    line_h = max(1, self.view.font.get_height() + 2)
                    delta = -event.y * (line_h * 3 // 2)
                    new_scroll = self.model.scroll_y + delta
                    logger.debug(f"[ItemsPropertiesPanel] wheel pos=({mx},{my}) y={event.y} line_h={line_h} delta={delta} prev={self.model.scroll_y} max_scroll={max_scroll}")
                    if max_scroll is None:
                        self.model.scroll_y = max(0, new_scroll)
                    else:
                        self.model.scroll_y = max(0, min(new_scroll, max_scroll))
                    logger.debug(f"[ItemsPropertiesPanel] wheel applied scroll_y={self.model.scroll_y}")
                    return

        # Soporte para ruedas antiguas como botones 4/5
        if event.type == pygame.MOUSEBUTTONDOWN and event.button in (4, 5):
            panel = getattr(self.model, 'panel_rect', None)
            if panel:
                mx, my = pygame.mouse.get_pos()
                if panel.collidepoint(mx, my):
                    content_h = getattr(self.model, 'content_height', 0)
                    view_h = max(0, panel.h - 20)
                    if content_h > 0 and content_h <= view_h:
                        logger.debug(f"[ItemsPropertiesPanel] btn4/5 ignored (no overflow) content_h={content_h} view_h={view_h}")
                        self.model.scroll_y = 0
                        return
                    max_scroll = max(0, content_h - view_h) if content_h > 0 else None
                    line_h = max(1, self.view.font.get_height() + 2)
                    wheel_y = 1 if event.button == 4 else -1
                    delta = -wheel_y * (line_h * 3 // 2)
                    new_scroll = self.model.scroll_y + delta
                    logger.debug(f"[ItemsPropertiesPanel] btn4/5 pos=({mx},{my}) btn={event.button} line_h={line_h} delta={delta} prev={self.model.scroll_y} max_scroll={max_scroll}")
                    if max_scroll is None:
                        self.model.scroll_y = max(0, new_scroll)
                    else:
                        self.model.scroll_y = max(0, min(new_scroll, max_scroll))
                    logger.debug(f"[ItemsPropertiesPanel] btn4/5 applied scroll_y={self.model.scroll_y}")
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
