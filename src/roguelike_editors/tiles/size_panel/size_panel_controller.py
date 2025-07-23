from roguelike_editors.tiles.size_panel.size_panel_view import SizePanelView

class SizePanelController:
    """
    Controller for the Size Panel.
    """
    def __init__(self, editor_controller, state):
        self.editor_controller = editor_controller
        self.state = state
        self.view = SizePanelView(self, state)

    def toggle(self):
        """
        Toggle visibility of the size panel.
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

    def on_size_selected(self, index):
        """
        Handle selection of size at given index.
        """
        self.state.select(index)
        # Panel remains open after selection