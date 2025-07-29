import pygame
from roguelike_ui.widgets.text_input import TextInput
from roguelike_ui.widgets.double_click_detector import DoubleClickDetector
from roguelike_editors.entities.entities_properties_panel.entities_properties_panel_model import EntityPropertiesPanelModel


class EntitiesPropertiesPanelEventHandler:
    """
    Manejador de eventos para el panel de propiedades.

    Responsable de:
    - Manejar edición de propiedades (doble clic + TextInput).
    - Detección de hover en propiedades.
    - Drag del panel (botón derecho).
    """

    def __init__(self, controller):
        self.controller = controller
        self.model: EntityPropertiesPanelModel = controller.model
        self.view = controller.view
        self.text_input = TextInput(controller.view.font)
        self.dc_detector = DoubleClickDetector()

    # ----------------------------
    # ENTRADA PRINCIPAL DE EVENTOS
    # ----------------------------
    def handle(self, event: pygame.event.Event) -> bool:
        """Procesa un evento Pygame y devuelve True si fue consumido."""
        # 1. Si hay edición activa, delegamos al TextInput
        if self._handle_active_text_edit(event):
            return True

        # 2. Verificamos que el panel sea interactivo
        if not self.model.selected_id or not self.model.panel_rect:
            return False

        # 3. Eventos relacionados con drag
        if self._handle_drag_events(event):
            return True

        # 4. Hover sobre propiedades
        if self._handle_hover(event):
            return True

        # 5. Eventos de teclado (cancelar edición)
        if self._handle_key_events(event):
            return True

                # 6. Clic en pestañas
        if self._handle_tab_click(event):
            return True


        # 7. Eventos de grid (subtabs y celdas)
        if self.model.active_tab == 'assets':
            if self.controller.grid_controller.handle_event(event):
                return True

        # 7. Clic en propiedades (single/double click)
        if self._handle_property_click(event):
            return True

        return False

    # ----------------------------
    # EDICIÓN DE TEXTO
    # ----------------------------
    def _handle_active_text_edit(self, event: pygame.event.Event) -> bool:
        """Maneja eventos cuando hay un TextInput activo."""
        if self.text_input.active:
            if self.text_input.handle_event(event):
                # Actualizar estado del modelo en tiempo real
                self.model.editing_text = self.text_input.text
                self.model.editing_cursor = self.text_input.cursor

                # Si se desactivó el input, confirmar cambios
                if not self.text_input.active:
                    self.controller._commit_edit()
                return True
        return False

    # ----------------------------
    # DRAG DEL PANEL
    # ----------------------------
    def _handle_drag_events(self, event: pygame.event.Event) -> bool:
        """Maneja eventos para arrastrar el panel con botón derecho."""
        # Iniciar drag
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 3:
            if self.model.panel_rect.collidepoint(event.pos):
                self.view.draggable_panel.handle_event(event, header_rect=self.model.panel_rect)
                return True

        # Movimiento durante drag
        if event.type == pygame.MOUSEMOTION and self.view.draggable_panel.dragging:
            self.view.draggable_panel.handle_event(event)
            return True

        # Fin de drag
        if event.type == pygame.MOUSEBUTTONUP and self.view.draggable_panel.dragging:
            self.view.draggable_panel.handle_event(event)
            return True

        return False

    # ----------------------------
    # HOVER EN PROPIEDADES
    # ----------------------------
    def _handle_hover(self, event: pygame.event.Event) -> bool:
        """Detecta hover en propiedades o celdas de assets y actualiza el modelo."""
        # Skip hover handling on assets tab; let grid controller handle it
        if self.model.active_tab == 'assets':
            return False
        if event.type == pygame.MOUSEMOTION and self.model.panel_rect.collidepoint(event.pos):
            mx, my = event.pos
            if self.model.active_tab == 'assets':
                hovered = None
                for rect, key in self.model.asset_cell_entries:
                    if rect.collidepoint(mx, my):
                        hovered = key
                        break
                self.model.hovered_asset_cell = hovered
            else:
                hovered = None
                for rect, key in self.model.property_entries:
                    if rect.collidepoint(mx, my):
                        hovered = key
                        break
                self.model.hovered_property = hovered
            return True
        return False

    # ----------------------------
    # TECLAS ESPECIALES
    # ----------------------------
    def _handle_key_events(self, event: pygame.event.Event) -> bool:
        """Maneja teclas especiales (ej. cancelar edición)."""
        if event.type == pygame.KEYDOWN:
            if event.key == pygame.K_ESCAPE and self.model.editing_property:
                # Cancelar edición en curso
                self.model.editing_property = None
                self.model.editing_text = ""
                self.model.editing_cursor = 0
                return True
        return False

    # ----------------------------
    # CLIC SOBRE PROPIEDADES
    # ----------------------------
    def _handle_property_click(self, event: pygame.event.Event) -> bool:
        """Maneja clic simple o doble sobre propiedades."""
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
            mx, my = event.pos

            # Solo procesar clics dentro del panel
            if not self.model.panel_rect or not self.model.panel_rect.collidepoint(mx, my):
                return False

            # Procesar clic en entradas
            for rect, key in self.model.property_entries:
                if rect.collidepoint(mx, my):
                    # Detección de doble clic
                    if getattr(event, 'clicks', 1) >= 2 or self.dc_detector.is_double_click(key):
                        self._start_editing(key)
                        return True
                    # Selección simple (enfocar propiedad)
                    self.model.focused_property = key
                    return True

            # Clic dentro del panel pero fuera de una entrada: consumir sin más
            return True
        return False

    # ----------------------------
    # INICIAR EDICIÓN DE PROPIEDAD
    # ----------------------------

    def _start_editing(self, key: str) -> None:
        """Prepara el TextInput para editar una propiedad específica."""
        self.model.focused_property = key
        self.model.editing_property = key

        # Obtener valor actual
        if self.model.selected_id in self.model.player_stats:
            val = self.model.player_stats[self.model.selected_id].get(key, "")
        else:
            val = self.model.monsters[self.model.selected_id].get(key, "")

        self.model.editing_text = str(val)
        self.model.editing_cursor = len(self.model.editing_text)

        # Activar input con valor actual
        self.text_input.activate(self.model.editing_text)

    # ----------------------------
    # Manejo de pestañas
    # ----------------------------
    def _handle_tab_click(self, event: pygame.event.Event) -> bool:
        """Gestiona clics en las pestañas del panel."""
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1 and self.model.tab_rects:
            pos = event.pos
            for label, rect in self.model.tab_rects.items():
                if rect.collidepoint(pos):
                    # Cambiar pestaña y resetear estado de propiedad
                    self.model.active_tab = label
                    self.model.focused_property = None
                    self.model.editing_property = None
                    self.model.hovered_property = None
                    return True
        return False

