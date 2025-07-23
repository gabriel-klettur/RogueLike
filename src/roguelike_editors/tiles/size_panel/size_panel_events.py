import pygame

class SizePanelEventHandler:
    """
    Event handler for the Size Size Panel.
    """
    def __init__(self, controller, state):
        self.controller = controller
        self.state = state

    def handle_event(self, ev):
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