import pygame


class TilesCollisionPanelEventHandler:
    """
    Manejador de eventos para el panel de colisiones de tiles.
    Separa la lógica de selección y arrastre en métodos claros.
    """
    def __init__(self, controller, state):
        self.controller = controller
        self.state = state

    def handle_event(self, ev, *args, **kwargs):
        """
        Enrutador principal de eventos:
        - Click izquierdo: selección de opción de colisión
        - Click derecho: arrastre del panel
        - Movimiento del ratón: drag
        - Soltar botón derecho: detener drag
        Devuelve True si el evento fue consumido.
        """
        if ev.type == pygame.MOUSEBUTTONDOWN and ev.button == 1:
            return self._select_collision(ev.pos)

        if ev.type == pygame.MOUSEBUTTONDOWN and ev.button == 3:
            return self._start_drag(ev.pos)

        if ev.type == pygame.MOUSEMOTION:
            return self._drag(ev.pos)

        if ev.type == pygame.MOUSEBUTTONUP and ev.button == 3:
            return self._stop_drag()

        return False

    def _select_collision(self, pos):
        """
        Selecciona la opción de colisión clickeada.
        """
        toolbar_state = self.controller.editor_state.toolbar_state
        for choice, rect in self.state.option_rects.items():
            if rect.collidepoint(pos):
                toolbar_state.collision_choice = choice
                return True
        return False

    def _start_drag(self, pos):
        """
        Inicia arrastre si el click derecho está dentro del panel.
        Guarda posición inicial y offset.
        """
        toolbar_state = self.controller.editor_state.toolbar_state
        x0, y0 = toolbar_state.collision_picker_pos
        w, h = toolbar_state.collision_picker_panel_size
        panel_rect = pygame.Rect(x0, y0, w, h)

        if panel_rect.collidepoint(pos):
            toolbar_state.collision_picker_dragging = True
            toolbar_state.collision_picker_drag_offset = (pos[0] - x0, pos[1] - y0)
            return True
        return False

    def _drag(self, pos):
        """
        Mueve el panel mientras se arrastra.
        """
        toolbar_state = self.controller.editor_state.toolbar_state
        if toolbar_state.collision_picker_dragging:
            offset_x, offset_y = toolbar_state.collision_picker_drag_offset
            toolbar_state.collision_picker_pos = (pos[0] - offset_x, pos[1] - offset_y)
            return True
        return False

    def _stop_drag(self):
        """
        Detiene el arrastre al soltar el botón derecho.
        """
        toolbar_state = self.controller.editor_state.toolbar_state
        if toolbar_state.collision_picker_dragging:
            toolbar_state.collision_picker_dragging = False
            return True
        return False
