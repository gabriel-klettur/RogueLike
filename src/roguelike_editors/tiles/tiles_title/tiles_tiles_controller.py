from roguelike_editors.tiles.tiles_title.tiles_tiles_view import TilesTilesView

class TilesTitleController:
    """Controller for the Tiles Title Panel"""
    def __init__(self, editor_state, state):
        self.editor_state = editor_state
        self.state = state
        # Panel view
        self.view = TilesTilesView(self, state)

    def render(self, screen):
        # Delegate rendering to panel view and return rect for layout
        return self.view.render(screen)
