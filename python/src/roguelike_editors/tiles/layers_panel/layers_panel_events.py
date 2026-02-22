import pygame
from roguelike_engine.map.model.layer import Layer
from roguelike_editors.tiles.tiles_editor_config import BTN_W, BTN_H, PAD


class LayersPanelEventHandler:
    """
    Manejador de eventos para el panel de capas.
    Separa lógica de arrastre y toggles de visibilidad en métodos claros.
    """
    def __init__(self, controller, state):
        self.controller = controller
        self.state = state

    def handle_event(self, ev, *args, **kwargs):
        """
        Enrutador principal de eventos: derecha=arrastre, izquierdo=toggle de capas.
        Devuelve True si el evento fue consumido.
        """
        # Drag & drop con botón derecho
        if ev.type == pygame.MOUSEBUTTONDOWN and ev.button == 3:
            return self._start_drag(ev.pos)
        if ev.type == pygame.MOUSEMOTION and self.state.dragging:
            return self._drag(ev.pos)
        if ev.type == pygame.MOUSEBUTTONUP and ev.button == 3 and self.state.dragging:
            return self._stop_drag()

        # Toggle de visibilidad con botón izquierdo
        if ev.type == pygame.MOUSEBUTTONDOWN and ev.button == 1:
            return self._toggle_layer_at(ev.pos)

        return False

    def _start_drag(self, mouse_pos):
        """
        Inicia arrastre si el click derecho está dentro del área del panel.
        Calcula posición inicial y registra offset.
        """
        x0, y0 = self._initial_position()
        panel_h = (len(list(Layer)) + 1) * BTN_H
        panel_rect = pygame.Rect(x0, y0, BTN_W, panel_h)

        if panel_rect.collidepoint(mouse_pos):
            self.state.dragging = True
            self.state.drag_offset = (mouse_pos[0] - x0, mouse_pos[1] - y0)
            return True
        return False

    def _drag(self, mouse_pos):
        """
        Mueve el panel mientras se arrastra.
        """
        self.controller.drag(mouse_pos)
        return True

    def _stop_drag(self):
        """
        Finaliza arrastre al soltar el botón derecho.
        """
        self.controller.stop_drag()
        return True

    def _toggle_layer_at(self, mouse_pos):
        """
        Alterna visibilidad de la capa cuyo rect clickeado corresponde.
        """
        for key, rect in self.state.option_rects.items():
            if rect.collidepoint(mouse_pos):
                if key == "buildings":
                    self._toggle_buildings()
                else:
                    self._toggle_generic_layer(key)
                return True
        return False

    def _toggle_buildings(self):
        """
        Alterna visibilidad de edificios en la barra de herramientas.
        """
        ts = self.controller.editor_state.toolbar_state
        ts.show_buildings = not ts.show_buildings

    def _toggle_generic_layer(self, key):
        """
        Alterna visibilidad de cualquier otra capa y actualiza el estado.
        """
        new_val = not self.state.visible_layers[key]
        self.state.visible_layers[key] = new_val
        self.controller.editor_state.toolbar_state.visible_layers[key] = new_val

    def _initial_position(self):
        """
        Calcula la posición (x0, y0) del panel de capas al iniciar.
        Usa la posición guardada o se basa en el view_panel_state.
        """
        if self.state.pos is not None:
            return self.state.pos

        vp = self.controller.editor_state.view_panel_state
        # Si existen pos y size definidos, colocar debajo del view panel
        if getattr(vp, 'pos', None) and getattr(vp, 'size', None):
            x0 = vp.pos[0]
            y0 = vp.pos[1] + vp.size[1] + PAD
            return (x0, y0)

        # Posición por defecto
        return (20, 60)
