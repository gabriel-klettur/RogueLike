import pygame
from roguelike_ui.widgets.text_input import TextInput
from roguelike_ui.widgets.double_click_detector import DoubleClickDetector
from roguelike_editors.entities.entities_properties_panel.entities_properties_panel_model import EntityPropertiesPanelModel

class EntitiesPropertiesPanelEventHandler:
    """Manejador de eventos para el panel de propiedades."""
    def __init__(self, controller):
        self.controller = controller
        self.model: EntityPropertiesPanelModel = controller.model
        self.view = controller.view
        self.text_input = TextInput(controller.view.font)
        self.dc_detector = DoubleClickDetector()

    def handle(self, event: pygame.event.Event) -> bool:
        # Manejo de edición de texto activo
        if self.text_input.active:
            if self.text_input.handle_event(event):
                self.model.editing_text = self.text_input.text
                self.model.editing_cursor = self.text_input.cursor
                if not self.text_input.active:
                    self.controller._commit_edit()
                return True
            return False

        # Solo si hay panel y es visible
        if not self.model.selected_id or not self.model.panel_rect:
            return False
        # Drag start: botón derecho en cualquier parte del panel
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 3 and self.model.panel_rect and self.model.panel_rect.collidepoint(event.pos):
            self.view.draggable_panel.handle_event(event, header_rect=self.model.panel_rect)
            return True
        # Drag move
        if event.type == pygame.MOUSEMOTION and self.view.draggable_panel.dragging:
            self.view.draggable_panel.handle_event(event)
            return True
        # Drag end
        if event.type == pygame.MOUSEBUTTONUP and self.view.draggable_panel.dragging:
            self.view.draggable_panel.handle_event(event)
            return True
        # Key events para cancelar edición o navegación
        if event.type == pygame.KEYDOWN:
            if event.key == pygame.K_ESCAPE and self.model.editing_property:
                # cancelar edición
                self.model.editing_property = None
                self.model.editing_text = ""
                self.model.editing_cursor = 0
                return True
        # Click sobre propiedades
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
            mx, my = event.pos
            # Verificar clic dentro del panel
            if self.model.panel_rect.collidepoint(mx, my):
                for rect, key in self.model.property_entries:
                    if rect.collidepoint(mx, my):
                        # doble click
                        if getattr(event, 'clicks', 1) >= 2 or self.dc_detector.is_double_click(key):
                            self.model.focused_property = key
                            self.model.editing_property = key
                            # Prefill valor
                            if self.model.selected_id in self.model.player_stats:
                                val = self.model.player_stats[self.model.selected_id].get(key, "")
                            else:
                                val = self.model.monsters[self.model.selected_id].get(key, "")
                            self.model.editing_text = str(val)
                            self.model.editing_cursor = len(self.model.editing_text)
                            self.text_input.activate(self.model.editing_text)
                            return True
                        else:
                            self.model.focused_property = key
                            return True
                return False
        return False
