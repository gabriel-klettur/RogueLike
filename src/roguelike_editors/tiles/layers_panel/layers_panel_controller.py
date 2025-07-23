from roguelike_editors.tiles.layers_panel.layers_panel_view import LayersPanelView

class LayersPanelController:
    """Controller for the Layers Panel"""
    def __init__(self, editor_state, state):
        self.editor_state = editor_state
        self.state = state
        # Panel view
        self.view = LayersPanelView(self, state)
        # Initialize default visibility from toolbar state
        self.state.visible_layers = dict(self.editor_state.toolbar_state.visible_layers)

    def render(self, screen):
        # Delegate rendering to panel view
        self.view.render(screen)

    def drag(self, mouse_pos):
        """
        Update panel position durante arrastre.
        """
        if self.state.dragging:
            self.state.pos = (mouse_pos[0] - self.state.drag_offset[0], mouse_pos[1] - self.state.drag_offset[1])

    def stop_drag(self):
        """
        Finaliza arrastre del panel.
        """
        self.state.dragging = False
