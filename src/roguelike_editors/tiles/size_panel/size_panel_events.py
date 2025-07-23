import pygame
from roguelike_editors.tiles.tiles_editor_config import BTN_W, BTN_H, PAD

class SizePanelEventHandler:
    """
    Event handler for the Size Size Panel.
    """
    def __init__(self, controller, state):
        self.controller = controller
        self.state = state

    def handle_event(self, ev):

        # Start dragging panel with right mouse button
        if ev.type == pygame.MOUSEBUTTONDOWN and ev.button == 3:
            mouse_pos = ev.pos
            # Compute initial panel position
            if self.state.pos is not None:
                x0, y0 = self.state.pos
            else:
                toolbar = self.controller.editor_controller.toolbar
                x0 = toolbar.x + toolbar.size + toolbar.padding
                y0 = toolbar.y
            # Determine panel height
            panel_h = len(self.state.sizes) * BTN_H
            panel_rect = pygame.Rect(x0, y0, BTN_W, panel_h)
            if panel_rect.collidepoint(mouse_pos):
                self.state.dragging = True
                self.state.drag_offset = (mouse_pos[0] - x0, mouse_pos[1] - y0)
                return True

        # Handle dragging movement
        if ev.type == pygame.MOUSEMOTION and self.state.dragging:
            self.controller.drag(ev.pos)
            return True

        # Stop drag on right button release
        if ev.type == pygame.MOUSEBUTTONUP and ev.button == 3 and self.state.dragging:
            self.controller.stop_drag()
            return True
        """
        Process click events on the size panel.
        Returns True if the event is consumed.
        """
        if not self.state.visible:
            return False
        if ev.type == pygame.MOUSEBUTTONDOWN and ev.button == 1:
            mouse_pos = ev.pos
            for idx, rect in self.state.option_rects.items():
                if rect.collidepoint(mouse_pos):
                    self.controller.on_size_selected(idx)
                    return True
        return False