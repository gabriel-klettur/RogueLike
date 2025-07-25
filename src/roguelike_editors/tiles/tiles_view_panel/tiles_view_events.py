import pygame
from roguelike_editors.tiles.tiles_editor_config import BTN_H


class TilesViewPanelEventHandler:
    """
    Manejador de eventos para el panel de vista de tiles.

    Controla el arrastre (drag & drop) del panel de vista con el botón derecho del ratón.
    """
    def __init__(self, controller, state):
        """
        Inicializa el manejador con el controlador de la vista y su estado asociado.

        Args:
            controller: Controlador que maneja la lógica de la vista.
            state: Objeto que almacena el estado (posición, tamaño, flags de arrastre).
        """
        self.controller = controller
        self.state = state

    def handle_event(self, ev, *args, **kwargs) -> bool:
        """
        Procesa eventos de pygame para habilitar drag & drop con botón derecho.

        - Inicia arrastre al presionar botón derecho sobre el panel.
        - Mueve el panel mientras se arrastra.
        - Finaliza arrastre al soltar el botón derecho.

        Args:
            ev: Evento de pygame recibido.
        Returns:
            True si el evento fue consumido, False en caso contrario.
        """
        if self._is_right_click_start(ev):
            return self._start_drag(ev.pos)
        if self._is_drag_motion(ev):
            return self._perform_drag(ev.pos)
        if self._is_right_click_end(ev):
            return self._stop_drag()
        return False

    def _is_right_click_start(self, ev) -> bool:
        """
        Determina si el evento inicia un arrastre (MOUSEBUTTONDOWN derecho).
        """
        return ev.type == pygame.MOUSEBUTTONDOWN and ev.button == 3

    def _is_drag_motion(self, ev) -> bool:
        """
        Comprueba si el evento es un movimiento del ratón durante arrastre.
        """
        return ev.type == pygame.MOUSEMOTION and self.state.dragging

    def _is_right_click_end(self, ev) -> bool:
        """
        Verifica si el evento finaliza el arrastre (MOUSEBUTTONUP derecho).
        """
        return ev.type == pygame.MOUSEBUTTONUP and ev.button == 3 and self.state.dragging

    def _start_drag(self, mouse_pos: tuple[int, int]) -> bool:
        """
        Inicia el arrastre si la posición del click cae dentro del panel.

        Calcula la posición base del panel (override o default) y
        registra el offset para arrastre suave.
        """
        x0, y0 = self._get_initial_position()
        if not self.state.size:
            return False
        panel_w, panel_h = self.state.size
        panel_rect = pygame.Rect(x0, y0, panel_w, panel_h)
        if panel_rect.collidepoint(mouse_pos):
            self.state.dragging = True
            self.state.drag_offset = (mouse_pos[0] - x0, mouse_pos[1] - y0)
            return True
        return False

    def _perform_drag(self, mouse_pos: tuple[int, int]) -> bool:
        """
        Actualiza la posición del panel mientras se arrastra.

        Args:
            mouse_pos: Posición actual del cursor.
        """
        self.controller.drag(mouse_pos)
        return True

    def _stop_drag(self) -> bool:
        """
        Finaliza el arrastre y desactiva el flag correspondiente.
        """
        self.controller.stop_drag()
        return True

    def _get_initial_position(self) -> tuple[int, int]:
        """
        Calcula la posición inicial del panel.

        Prioriza la posición almacenada en state.pos, si no existe,
        calcula la posición por defecto anclada a la esquina superior derecha.
        """
        if self.state.pos is not None:
            return self.state.pos
        surf = pygame.display.get_surface()
        if not surf or not self.state.size:
            return (0, 0)
        sw, _ = surf.get_size()
        panel_w, _ = self.state.size
        margin = 12
        x0 = sw - panel_w - margin
        y0 = margin
        return (x0, y0)
