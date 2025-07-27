import pygame


class EntitiesPickerEventHandler:
    """
    Manejador de eventos para el panel de selección de entidades en el editor.
    
    Permite:
    - Mostrar/ocultar el panel con F5.
    - Desplazarse por el grid con teclas de dirección.
    - Seleccionar entidades con clic izquierdo.
    - Detectar hover sobre celdas.
    - Arrastrar el panel con botón derecho (drag).
    """

    def __init__(self, controller):
        self.controller = controller
        self.model = controller.model
        self.view = controller.view

    # ----------------------------
    # MÉTODO PRINCIPAL
    # ----------------------------
    def handle(self, event: pygame.event.Event) -> None:
        """Maneja los eventos de Pygame relacionados con el panel de selección."""
        
        # Manejo del arrastre del panel
        if self._handle_panel_drag(event):
            return

        # Toggle de visibilidad y navegación por teclado
        if event.type == pygame.KEYDOWN:
            self._handle_keydown(event)
            return

        # Clic izquierdo para selección en el grid
        if event.type == pygame.MOUSEBUTTONDOWN and self.model.visible and event.button == 1:
            self._handle_left_click(event.pos)
            return

        # Movimiento del mouse para hover
        if event.type == pygame.MOUSEMOTION and self.model.visible:
            self._handle_hover(event.pos)
            return

        # Si no hay hover, se resetea
        self.model.hovered_id = None

    # ----------------------------
    # EVENTOS DE PANEL (DRAG)
    # ----------------------------
    def _handle_panel_drag(self, event: pygame.event.Event) -> bool:
        """Maneja el arrastre del panel (botón derecho)."""
        
        if not self.model.visible:
            return False

        # Inicio de arrastre
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 3:
            if self.model.panel_rect and self.model.panel_rect.collidepoint(event.pos):
                self.view.draggable_panel.handle_event(event, header_rect=self.model.panel_rect)
                return True

        # Movimiento durante arrastre
        if event.type == pygame.MOUSEMOTION and self.view.draggable_panel.dragging:
            self.view.draggable_panel.handle_event(event)
            return True

        # Fin de arrastre
        if event.type == pygame.MOUSEBUTTONUP and self.view.draggable_panel.dragging:
            self.view.draggable_panel.handle_event(event)
            return True

        return False

    # ----------------------------
    # EVENTOS DE TECLADO
    # ----------------------------
    def _handle_keydown(self, event: pygame.event.Event) -> None:
        """Maneja eventos de teclado (F5 para toggle, flechas para scroll)."""
        
        if event.key == pygame.K_F5:
            # Mostrar/ocultar panel
            self.model.visible = not self.model.visible
            self.model.selected_id = None
            return

        if not self.model.visible:
            return

        if event.key == pygame.K_UP:
            self.model.scroll_index = max(0, self.model.scroll_index - 1)
            return

        if event.key == pygame.K_DOWN:
            self.model.scroll_index += 1
            return

    # ----------------------------
    # SELECCIÓN EN GRID
    # ----------------------------
    def _handle_left_click(self, pos: tuple[int, int]) -> None:
        """Detecta selección de entidad al hacer clic izquierdo en el grid."""
        col, row, idx, entity_ids = self._calculate_grid_position(pos)
        
        if col is None or idx is None:
            self.model.selected_id = None
            return

        # Verificar si está dentro de los límites
        if idx < len(entity_ids) and self._is_within_cell_bounds(pos, col, row):
            self.model.selected_id = entity_ids[idx]
        else:
            self.model.selected_id = None

    # ----------------------------
    # DETECCIÓN DE HOVER
    # ----------------------------
    def _handle_hover(self, pos: tuple[int, int]) -> None:
        """Detecta qué entidad está bajo el cursor (hover)."""
        col, row, idx, entity_ids = self._calculate_grid_position(pos)

        if col is None or idx is None:
            self.model.hovered_id = None
            return

        if idx < len(entity_ids) and self._is_within_cell_bounds(pos, col, row):
            self.model.hovered_id = entity_ids[idx]
        else:
            self.model.hovered_id = None

    # ----------------------------
    # UTILIDADES INTERNAS
    # ----------------------------
    def _calculate_grid_position(self, pos: tuple[int, int]):
        """
        Calcula columna, fila e índice del grid basado en la posición del mouse.
        
        Retorna:
            (col, row, idx, entity_ids)
        """
        mx, my = pos
        ox, oy = self.view.x, self.view.y
        margin = self.view.margin
        cell_size = self.view.cell_size
        tm = self.view.text_margin
        fh = self.view.font.get_height()
        ch = cell_size + tm + fh
        cols = self.view.columns

        # Ajuste relativo
        mx_rel = mx - (ox + margin)
        my_rel = my - (oy + margin)

        if mx_rel < 0 or my_rel < 0:
            return None, None, None, []

        col = mx_rel // (cell_size + margin)
        row = my_rel // (ch + margin) + self.model.scroll_index
        entity_ids = list(self.model.player_stats.keys()) + list(self.model.monsters.keys())
        idx = row * cols + col

        return col, row, idx, entity_ids

    def _is_within_cell_bounds(self, pos: tuple[int, int], col: int, row: int) -> bool:
        """Comprueba si la posición está dentro de los límites exactos de la celda."""
        mx, my = pos
        ox, oy = self.view.x, self.view.y
        margin = self.view.margin
        cell_size = self.view.cell_size
        tm = self.view.text_margin
        fh = self.view.font.get_height()
        ch = cell_size + tm + fh

        x0 = ox + margin + col * (cell_size + margin)
        y0 = oy + margin + (row - self.model.scroll_index) * (ch + margin)

        return x0 <= mx <= x0 + cell_size and y0 <= my <= y0 + cell_size
