class LayersPanelEventHandler:
    """Event handler for the Layers Panel"""
    def __init__(self, controller, state):
        self.controller = controller
        self.state = state

    def handle_event(self, ev, *args, **kwargs):
        import pygame

        # Toggle layer visibility on click
        if ev.type == pygame.MOUSEBUTTONDOWN and ev.button == 1:
            mouse_pos = ev.pos
            for layer, rect in self.state.option_rects.items():
                if rect.collidepoint(mouse_pos):
                    new_val = not self.state.visible_layers[layer]
                    self.state.visible_layers[layer] = new_val
                    # sync with toolbar state
                    self.controller.editor_state.toolbar_state.visible_layers[layer] = new_val
                    break
