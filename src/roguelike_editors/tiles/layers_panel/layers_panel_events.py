import pygame
from roguelike_engine.map.model.layer import Layer
from roguelike_editors.tiles.tiles_editor_config import BTN_W, BTN_H, PAD

class LayersPanelEventHandler:
    """Event handler for the Layers Panel"""
    def __init__(self, controller, state):
        self.controller = controller
        self.state = state

    def handle_event(self, ev, *args, **kwargs):

        # Start dragging panel with right mouse button
        if ev.type == pygame.MOUSEBUTTONDOWN and ev.button == 3:
            mouse_pos = ev.pos
            # Compute initial panel position
            if self.state.pos is not None:
                x0, y0 = self.state.pos
            else:
                vp_state = self.controller.editor_state.view_panel_state
                if hasattr(vp_state, 'pos') and hasattr(vp_state, 'size') and vp_state.pos and vp_state.size:
                    x0 = vp_state.pos[0]
                    y0 = vp_state.pos[1] + vp_state.size[1] + PAD
                else:
                    x0, y0 = 20, 60
            # Determine panel height
            panel_h = (len(list(Layer)) + 1) * BTN_H
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
