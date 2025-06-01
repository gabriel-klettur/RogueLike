from roguelike_game.systems.editor.map.map_editor_state import MapEditorState
from roguelike_game.systems.editor.map.map_editor_controller import MapEditorController
from roguelike_game.systems.editor.map.map_editor_events import MapEditorEventHandler
from roguelike_game.systems.editor.map.map_editor_view import MapEditorView

class MapEditorManager:
    """
    Manager para el Map Editor, análogo a TilesEditorManager y BuildingEditorManager.
    """
    def __init__(self, game):
        self.game = game
        self.editor_state = MapEditorState()
        self.controller = MapEditorController(self.editor_state, game.map)
        self.view = MapEditorView(self.controller, self.editor_state, game.map)
        # Pass self to handler so toggle logic resets zoom and recenter on exit
        self.handler = MapEditorEventHandler(self, self.editor_state, self.controller, game.map)

    def toggle(self):
        active = not self.editor_state.active
        self.editor_state.active = active
        # Reset zoom to minimum when entering Map Editor
        if active:
            self.game.camera.zoom = 0.5
        else:
            # Restore zoom and recenter camera on exit
            self.game.camera.zoom = 1.0
            self.game.camera.update(self.game.buildings.player)
            # reset de subestado al cerrar
            self.editor_state.selected_zone = None
            self.editor_state.hidden_zones.clear()
            self.editor_state.dragging = None
        print(" Map Editor ON" if active else " Map Editor OFF")

    def handle(self, camera, map_manager):
        if self.editor_state.active:
            self.handler.handle(camera, map_manager)

    def update(self, camera, map_manager):
        # por ahora no hay lógica de actualización adicional
        pass

    def render(self, screen, camera, map_manager):
        if self.editor_state.active:
            self.view.render(screen, camera, map_manager)