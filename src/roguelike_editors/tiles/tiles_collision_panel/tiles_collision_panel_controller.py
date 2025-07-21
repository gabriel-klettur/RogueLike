from roguelike_editors.tiles.tiles_collision_panel.tiles_collision_panel_view import TilesCollisionPanelView

class TilesCollisionPanelController:
    """Controller for the Tiles Collision Panel"""
    def __init__(self, editor_state, state):
        self.editor_state = editor_state
        self.state = state
        # Panel view
        self.view = TilesCollisionPanelView(self, state)

    def render(self, screen):
        # Delegate rendering to panel view
        self.view.render(screen)
        pass
