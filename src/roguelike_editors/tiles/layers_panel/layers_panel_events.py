import pygame

class LayersPanelEventHandler:
    """Event handler for the Layers Panel"""
    def __init__(self, controller, state):
        self.controller = controller
        self.state = state

    def handle_event(self, ev, *args, **kwargs):

        # Toggle layer visibility on click
        if ev.type == pygame.MOUSEBUTTONDOWN and ev.button == 1:
            mouse_pos = ev.pos
            for key, rect in self.state.option_rects.items():
                if rect.collidepoint(mouse_pos):
                    if key == "buildings":
                        ts = self.controller.editor_state.toolbar_state
                        ts.show_buildings = not ts.show_buildings
                    else:
                        new_val = not self.state.visible_layers[key]
                        self.state.visible_layers[key] = new_val
                        self.controller.editor_state.toolbar_state.visible_layers[key] = new_val
                    break
            return True
