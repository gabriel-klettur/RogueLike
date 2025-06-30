
# Path: src/roguelike_game/game/tiles_editor_manager.py
from roguelike_game.systems.editor.tiles.model.tile_editor_state import TileEditorState
from roguelike_game.systems.editor.tiles.controller.tile_editor_controller import TileEditorController
from roguelike_game.systems.editor.tiles.events.tile_editor_events import TileEditorEventHandler
from roguelike_game.systems.editor.tiles.view.tile_editor_view import TileEditorView

class TilesEditorManager:
    def __init__(self, game):
        
        # Inicialización del editor de tiles
        self.editor_state = TileEditorState()
        self.controller   = TileEditorController(self.editor_state, self.editor_state.picker_state)
        self.view         = TileEditorView(self.controller, self.editor_state)
        self.handler      = TileEditorEventHandler(game.state, self.editor_state, self.controller)

    def toggle(self):
        """Activa/desactiva el editor de tiles y limpia sub-estado al cerrarlo."""
        active = not self.editor_state.active
        self.editor_state.active = active

        if not active:
            # reset de selección
            self.editor_state.picker_open       = False
            self.editor_state.selected_tile     = None
            self.editor_state.current_choice    = None

        print("🟩 Tile-Editor ON REAL!" if active else "🟥 Tile-Editor OFF")

    def handle(self, camera, game_map):
        """
        Enruta eventos al manejador del editor de tiles.
        """
        if self.editor_state.active:
            self.handler.handle(camera, game_map)

    def update(self, camera, game_map):
        """
        Actualiza el controlador si está activo.
        """
        if self.editor_state.active:
            self.controller.update(camera, game_map)

    def render(self, screen, camera, game_map):
        """
        Renderiza la vista del editor de tiles si está activo.
        """
        if self.editor_state.active:
            self.view.render(screen, camera, game_map)