from roguelike_editors.map.map_editor_state import MapEditorState
from roguelike_editors.map.map_editor_controller import MapEditorController
from roguelike_editors.map.map_editor_events import MapEditorEventHandler
from roguelike_editors.map.map_editor_view import MapEditorView
from roguelike_editors.map.map_tutorial_panel import MapTutorialPanelController
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.map.utils import get_zone_for_tile
from roguelike_engine.config.map_config import global_map_settings
from types import SimpleNamespace

import logging
logger = logging.getLogger(__name__)
import os
import json
from roguelike_engine.config.config import DATA_DIR

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
        # Panel de Tutorial (overlay con guía paso a paso)
        self.tutorial = MapTutorialPanelController(game.state, self.editor_state, self.view, self)
        # Load persisted camera for the editor (if any)
        try:
            self._load_persisted_camera()
        except Exception:
            # Never break initialization due to persistence issues
            pass
        # Inyecciones cruzadas
        try:
            # Permitir al event handler del editor delegar al panel de tutorial
            self.handler.tutorial = self.tutorial
        except Exception:
            pass
        try:
            # Permitir que el panel de Tutorial se alinee a la derecha del toolbar/título
            if hasattr(self.controller, 'toolbar') and hasattr(self.controller.toolbar, 'view'):
                self.tutorial.view.toolbar_view = self.controller.toolbar.view
                # Inyectar referencia al manager en el toolbar para que el botón 'map_tutorial' pueda togglear
                try:
                    self.controller.toolbar.editor_manager = self
                except Exception:
                    pass
        except Exception:
            pass

    # --- Persistence helpers (camera state across sessions) ---
    def _state_file_path(self) -> str:
        path = os.path.join(DATA_DIR, "editors")
        os.makedirs(path, exist_ok=True)
        return os.path.join(path, "map_editor_state.json")

    def _load_persisted_camera(self) -> None:
        fp = self._state_file_path()
        if os.path.exists(fp):
            with open(fp, "r", encoding="utf-8") as f:
                data = json.load(f)
            try:
                ox = float(data.get("offset_x", 0.0))
                oy = float(data.get("offset_y", 0.0))
                z = float(data.get("zoom", 1.0))
                self.editor_state.saved_editor_camera = (ox, oy, z)
                logger.debug(
                    f"[MapEditor] Loaded persisted camera: offset=({ox:.2f},{oy:.2f}) zoom={z:.3f}"
                )
            except Exception:
                # Ignore malformed file
                pass

    def _save_persisted_camera(self, ox: float, oy: float, z: float) -> None:
        fp = self._state_file_path()
        data = {"offset_x": ox, "offset_y": oy, "zoom": z}
        try:
            with open(fp, "w", encoding="utf-8") as f:
                json.dump(data, f, ensure_ascii=False, indent=2)
            logger.debug(
                f"[MapEditor] Saved persisted camera: offset=({ox:.2f},{oy:.2f}) zoom={z:.3f}"
            )
        except Exception:
            # Ignore I/O failures silently
            pass

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
            # Persist to disk for next sessions
            try:
                self._save_persisted_camera(cam.offset_x, cam.offset_y, cam.zoom)
            except Exception:
                pass
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
            # Render del panel de Tutorial por encima de todo
            try:
                self.tutorial.render(screen)
            except Exception:
                pass