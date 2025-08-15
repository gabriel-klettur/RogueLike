from roguelike_editors.map.map_editor_state import MapEditorState
from roguelike_editors.map.map_editor_controller import MapEditorController
from roguelike_editors.map.map_editor_events import MapEditorEventHandler
from roguelike_editors.map.map_editor_view import MapEditorView
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.map.utils import get_zone_for_tile
from roguelike_engine.config.map_config import global_map_settings
from types import SimpleNamespace

import logging
logger = logging.getLogger(__name__)

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
        cam = self.game.camera
        if active:
            # Guardar estado de cámara del juego (fuera del editor)
            self.editor_state.saved_game_camera = (cam.offset_x, cam.offset_y, cam.zoom)
            # Restaurar última cámara del editor si existe; si no, inicializar centrado
            if self.editor_state.saved_editor_camera:
                ox, oy, z = self.editor_state.saved_editor_camera
                cam.offset_x, cam.offset_y, cam.zoom = ox, oy, z
            else:
                # Zoom inicial cómodo para edición
                cam.zoom = 0.5
                # Centrar cámara en el centro de la zona actual del jugador (si disponible)
                player_tile = self.game.map._local_state.get("player_pos")
                if player_tile:
                    zone = get_zone_for_tile(player_tile[0], player_tile[1])
                    off_x, off_y = global_map_settings.zone_offsets.get(zone, (0, 0))
                    zone_w, zone_h = global_map_settings.zone_size
                    center_tx = off_x + zone_w // 2
                    center_ty = off_y + zone_h // 2
                    px = center_tx * TILE_SIZE + TILE_SIZE / 2
                    py = center_ty * TILE_SIZE + TILE_SIZE / 2
                    cam.update(SimpleNamespace(x=px, y=py))
        else:
            # Guardar estado de cámara del editor
            self.editor_state.saved_editor_camera = (cam.offset_x, cam.offset_y, cam.zoom)
            # Restaurar cámara del juego si estaba guardada; si no, fallback a comportamiento previo
            if self.editor_state.saved_game_camera:
                ox, oy, z = self.editor_state.saved_game_camera
                cam.offset_x, cam.offset_y, cam.zoom = ox, oy, z
            else:
                cam.zoom = 1.0
                try:
                    cam.update(self.game.ecs.ecs_world.player_position)
                except Exception:
                    pass
            # Diferir el follow automático 1 frame para no sobrescribir el estado restaurado
            try:
                self.editor_state.defer_follow_frames = max(1, getattr(self.editor_state, 'defer_follow_frames', 0))
            except Exception:
                self.editor_state.defer_follow_frames = 1
            # reset de subestado al cerrar
            self.editor_state.selected_zone = None
            self.editor_state.hidden_zones.clear()
            self.editor_state.dragging = None
        logger.debug(" Map Editor ON" if active else " Map Editor OFF")

    def handle(self, camera, map_manager, events=None):
        if self.editor_state.active:
            self.handler.handle(camera, map_manager, events)

    def update(self, camera, map_manager):
        # por ahora no hay lógica de actualización adicional
        pass

    def render(self, screen, camera, map_manager):
        if self.editor_state.active:
            self.view.render(screen, camera, map_manager)