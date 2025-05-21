# Path: src/roguelike_game/systems/editor/tiles/controller/tools/tile_picker_controller.py
import pygame
from pathlib import Path

from roguelike_engine.utils.loader import load_image
from roguelike_engine.config.config import ASSETS_DIR
from roguelike_engine.map.model.overlay.overlay_manager import load_overlay, save_overlay
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.tile.assets import load_base_tile_images
from roguelike_engine.config.map_config import global_map_settings

from roguelike_game.systems.editor.tiles.tiles_editor_config import (
    BASE_TILE_DIR,
    ARROW_UP_ICON,
    FOLDER_ICON,
    FILE_PATTERNS,
    THUMB,
    COLS,
    PAD
)

class TilePickerController:
    """
    Ventana flotante de selección de tiles y explorador de directorios.
    """

    def __init__(self, editor_state, picker_state):        
        self.editor_state = editor_state
        self.picker_state = picker_state

        # Directorio base y directorio actual para explorar
        self.base_dir = Path(ASSETS_DIR) / BASE_TILE_DIR
        self.current_dir = self.base_dir

        # Lista de entradas (valor, Surface, is_dir)
        self.assets = []
        self._load_assets()

    def _load_assets(self):
        """
        Rellena self.assets con:
         - Entrada ".." para subir (si no estamos en base)
         - Carpetas en current_dir
         - Archivos que casan con FILE_PATTERNS
        Cada entrada es tupla (value, surface, is_dir).
        """
        self.assets.clear()
        thumb_size = (THUMB, THUMB)

        # Flecha hacia arriba
        if self.current_dir != self.base_dir:
            arrow_surf = load_image(ARROW_UP_ICON, thumb_size)
            self.assets.append(("..", arrow_surf, True))

        # Subdirectorios
        for entry in sorted(self.current_dir.iterdir()):
            if entry.is_dir():
                folder_surf = load_image(FOLDER_ICON, thumb_size)
                self.assets.append((entry.name, folder_surf, True))

        # Archivos según patrones
        seen = {}
        for pattern in FILE_PATTERNS:
            for f in sorted(self.current_dir.glob(pattern)):
                key = f.name.lower()
                if key not in seen:
                    seen[key] = f

        for f in seen.values():
            rel_path = str(f.relative_to(Path(ASSETS_DIR)))
            try:
                surf = load_image(rel_path, thumb_size)
                self.assets.append((rel_path, surf, False))
            except Exception as e:
                print(f"[TilePicker] ERROR cargando {rel_path}: {e}")

    def is_over(self, mouse_pos) -> bool:
        if not self.picker_state.surface or not self.picker_state.pos:
            return False
        x0, y0 = self.picker_state.pos
        w, h = self.picker_state.surface.get_size()
        mx, my = mouse_pos
        return x0 <= mx <= x0 + w and y0 <= my <= y0 + h

    def drag(self, mouse_pos):
        if self.picker_state.dragging:
            self.picker_state.pos = (
                mouse_pos[0] - self.picker_state.drag_offset[0],
                mouse_pos[1] - self.picker_state.drag_offset[1]
            )

    def stop_drag(self):
        self.picker_state.dragging = False

    def scroll(self, dy):
        self.editor_state.scroll_offset = max(0, self.editor_state.scroll_offset - dy * 30)

    def _delete_tile(self, map):
        tile = self.editor_state.selected_tile
        if tile:
            # Eliminar sprite y limpiar overlay
            tile.sprite = pygame.Surface((TILE_SIZE, TILE_SIZE), pygame.SRCALPHA)
            tile.scaled_cache.clear()
            self._persist_overlay(tile, "", map)
        self._close()

    def _set_default(self, map):
        tile = self.editor_state.selected_tile
        if tile:
            # Restaurar sprite base según tipo de tile
            base_map = load_base_tile_images()
            imgs = base_map.get(tile.tile_type)
            sprite = imgs[0] if isinstance(imgs, list) else imgs
            tile.sprite = sprite
            tile.scaled_cache.clear()
            self._persist_overlay(tile, "", map)
        self._close()

    def _close(self):
        self.picker_state.open = False
        self.picker_state.current_choice = None
        self.picker_state.dragging = False

    def _persist_overlay(self, tile, code: str, map):
        # Calcular posición global del tile
        row = tile.y // TILE_SIZE
        col = tile.x // TILE_SIZE

        # Determinar zona según configuración
        zone_name = None
        zone_offset_x = zone_offset_y = 0
        for zn, (ox, oy) in global_map_settings.zone_offsets.items():
            if ox <= col < ox + global_map_settings.zone_width and oy <= row < oy + global_map_settings.zone_height:
                zone_name = zn
                zone_offset_x, zone_offset_y = ox, oy
                break
        if zone_name is None:
            zone_name = "no_zone"

        # Cargar o inicializar overlay de la zona
        zone_overlay = load_overlay(zone_name)
        if zone_overlay is None:
            if zone_name in global_map_settings.zone_offsets:
                h = global_map_settings.zone_height
                w = global_map_settings.zone_width
            else:
                h = len(map.tiles)
                w = len(map.tiles[0]) if map.tiles else 0
            zone_overlay = [["" for _ in range(w)] for _ in range(h)]

        # Calcular posición local dentro de la overlay de la zona
        if zone_name in global_map_settings.zone_offsets:
            local_row = row - zone_offset_y
            local_col = col - zone_offset_x
        else:
            local_row, local_col = row, col

        zone_overlay[local_row][local_col] = code

        save_overlay(zone_name, zone_overlay)
        print(f"📝 Overlay guardado en: {zone_name}.overlay.json")