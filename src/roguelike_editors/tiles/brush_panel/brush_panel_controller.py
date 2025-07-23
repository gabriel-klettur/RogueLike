from roguelike_editors.tiles.brush_panel.brush_panel_view import BrushPanelView

class BrushPanelController:
    """
    Controller for the Brush Size Panel.
    """
    def __init__(self, editor_controller, state):
        self.editor_controller = editor_controller
        self.state = state
        self.view = BrushPanelView(self, state)

    def toggle(self):
        """
        Toggle visibility of the brush size panel.
        """
        self.state.visible = not self.state.visible

    def show(self):
        self.state.visible = True

    def hide(self):
        self.state.visible = False

    def render(self, screen):
        """
        Delegate rendering to the panel view.
        """
        self.view.render(screen)

    def on_size_selected(self, index):
        """
        Handle selection of brush size at given index.
        """
        self.state.select(index)
        # Panel remains open after selection