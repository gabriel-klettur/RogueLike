import pygame
from roguelike_editors.tiles.tiles_editor_config import BTN_H

class TilesViewPanelEventHandler:
    """Event handler for the Tiles View Panel"""
    def __init__(self, controller, state):
        self.controller = controller
        self.state = state

    def handle_event(self, ev, *args, **kwargs):
        """Drag & drop para el panel de vista con botón derecho"""
        # Iniciar arrastre con botón derecho
        if ev.type == pygame.MOUSEBUTTONDOWN and ev.button == 3:
            mouse_pos = ev.pos
            # Calcular posición inicial (override o default)
            if self.state.pos is not None:
                x0, y0 = self.state.pos
            else:
                toolbar = self.controller.editor_controller.toolbar
                x0 = toolbar.x + toolbar.size + 20
                base_y = toolbar.y
                if self.controller.editor_controller.size_panel_controller.state.visible:
                    base_y += len(self.controller.editor_controller.size_panel_controller.state.sizes) * BTN_H + toolbar.padding
                y0 = base_y
            # Verificar tamaño del panel
            if not self.state.size:
                return False
            panel_w, panel_h = self.state.size
            panel_rect = pygame.Rect(x0, y0, panel_w, panel_h)
            if panel_rect.collidepoint(mouse_pos):
                self.state.dragging = True
                self.state.drag_offset = (mouse_pos[0] - x0, mouse_pos[1] - y0)
                return True
        # Movimiento durante arrastre
        if ev.type == pygame.MOUSEMOTION and self.state.dragging:
            self.controller.drag(ev.pos)
            return True
        # Detener arrastre al soltar botón
        if ev.type == pygame.MOUSEBUTTONUP and ev.button == 3 and self.state.dragging:
            self.controller.stop_drag()
            return True
        return False
