import pygame
from roguelike_editors.tiles.tiles_editor_config import BTN_W, BTN_H, PAD


class SizePanelEventHandler:
    """
    Manejador de eventos para el panel de tamaños (brush size).
    Separa la lógica de arrastre y selección de tamaño en métodos claros.
    """
    def __init__(self, controller, state):
        self.controller = controller
        self.state = state

    def handle_event(self, ev):
        """
        Procesa eventos del panel:
        - Click derecho: arrastre
        - Click izquierdo: selección de tamaño
        Devuelve True si el evento fue consumido.
        """
        # Procesar arrastre con botón derecho
        if ev.type == pygame.MOUSEBUTTONDOWN and ev.button == 3:
            return self._start_drag(ev.pos)
        if ev.type == pygame.MOUSEMOTION and self.state.dragging:
            return self._perform_drag(ev.pos)
        if ev.type == pygame.MOUSEBUTTONUP and ev.button == 3 and self.state.dragging:
            return self._stop_drag()

        # Procesar selección de tamaño con botón izquierdo
        if self.state.visible and ev.type == pygame.MOUSEBUTTONDOWN and ev.button == 1:
            return self._select_size(ev.pos)

        return False

    def _start_drag(self, mouse_pos):
        """
        Inicia arrastre si el click derecho está dentro del panel.
        Calcula posición inicial y registra offset.
        """
        x0, y0 = self._initial_position()
        panel_h = len(self.state.sizes) * BTN_H
        panel_rect = pygame.Rect(x0, y0, BTN_W, panel_h)

        if panel_rect.collidepoint(mouse_pos):
            self.state.dragging = True
            self.state.drag_offset = (mouse_pos[0] - x0, mouse_pos[1] - y0)
            return True
        return False

    def _perform_drag(self, mouse_pos):
        """
        Mueve el panel mientras se arrastra.
        """
        self.controller.drag(mouse_pos)
        return True

    def _stop_drag(self):
        """
        Detiene el arrastre al soltar el botón derecho.
        """
        self.controller.stop_drag()
        return True

    def _select_size(self, mouse_pos):
        """
        Detecta qué opción de tamaño fue clickeada y notifica al controlador.
        """
        for idx, rect in self.state.option_rects.items():
            if rect.collidepoint(mouse_pos):
                self.controller.on_size_selected(idx)
                return True
        return False

    def _initial_position(self):
        """
        Calcula la posición inicial del panel de tamaños.
        Usa la posición guardada o la sitúa junto a la toolbar.
        """
        if self.state.pos is not None:
            return self.state.pos

        toolbar = self.controller.editor_controller.toolbar
        x0 = toolbar.x + toolbar.size + toolbar.padding
        y0 = toolbar.y
        # Guardar para futuras referencias
        self.state.pos = (x0, y0)
        return (x0, y0)
