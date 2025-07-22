from roguelike_editors.tiles.tiles_view_panel.tiles_view_view import TilesViewPanelView


class TilesViewPanelController:
    """Controller for the Tiles View Panel"""
    def __init__(self, editor_controller, state):
        self.editor_controller = editor_controller
        self.editor_state = editor_controller.editor
        self.state = state
        # Panel view
        self.view = TilesViewPanelView(self, state)

    def render(self, screen, camera, game_map):
        # Delegate rendering to panel view
        self.view.render(screen, camera, game_map)

    def _tile_under_mouse(self, mouse_pos, camera, game_map):
        return self.editor_controller._tile_under_mouse(mouse_pos, camera, game_map)



