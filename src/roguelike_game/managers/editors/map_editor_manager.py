# Path: src/roguelike_game/game/map_editor_manager.py
from roguelike_game.systems.editors.map.state.map_editor_state import MapEditorState
from roguelike_game.systems.editors.map.controllers.map_editor_controller import MapEditorController
from roguelike_game.systems.editors.map.events.map_editor_events import MapEditorEventHandler
from roguelike_game.systems.editors.map.views.map_editor_view import MapEditorView
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.map.utils import get_zone_for_tile
from roguelike_engine.config.map_config import global_map_settings
from types import SimpleNamespace

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
            # Set initial zoom
            self.game.camera.zoom = 0.5
            # Center camera on the center of the player's current zone
            player_tile = self.game.map._local_state.get("player_pos")
            if player_tile:
                zone = get_zone_for_tile(player_tile[0], player_tile[1])
                off_x, off_y = global_map_settings.zone_offsets.get(zone, (0, 0))
                zone_w, zone_h = global_map_settings.zone_size
                center_tx = off_x + zone_w // 2
                center_ty = off_y + zone_h // 2
                px = center_tx * TILE_SIZE + TILE_SIZE / 2
                py = center_ty * TILE_SIZE + TILE_SIZE / 2
                self.game.camera.update(SimpleNamespace(x=px, y=py))
        else:
            # Restore zoom and recenter camera on exit
            self.game.camera.zoom = 1.0
            self.game.camera.update(self.game.ecs.ecs_world.player_position)
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