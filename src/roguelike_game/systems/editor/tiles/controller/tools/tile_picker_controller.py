# Path: src/roguelike_game/systems/editor/tiles/controller/tools/tile_picker_controller.py

import pygame
from pathlib import Path

from roguelike_engine.utils.loader import load_image
from roguelike_engine.config import ASSETS_DIR
from roguelike_engine.map.overlay.overlay_manager import save_overlay
from roguelike_engine.config_tiles import TILE_SIZE
from roguelike_engine.tiles.assets import load_base_tile_images

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

    def __init__(self, state, editor_state, picker_state):
        self.state = state
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

    def open_with_selection(self, choice_path):
        """
        Abre el picker y hace scroll hasta la miniatura cuyo valor coincide con choice_path.
        """
        self.picker_state.open = True
        self.picker_state.current_choice = choice_path
        for idx, entry in enumerate(self.assets):
            if entry[0] == choice_path:
                row = idx // COLS
                self.editor_state.scroll_offset = row * (THUMB + PAD)
                break

    def is_over(self, mouse_pos) -> bool:
        if not self.picker_state.surface or not self.picker_state.pos:
            return False
        x0, y0 = self.picker_state.pos
        w, h = self.picker_state.surface.get_size()
        mx, my = mouse_pos
        return x0 <= mx <= x0 + w and y0 <= my <= y0 + h

    def handle_click(self, mouse_pos, button, map):
        if not self.picker_state.open or self.picker_state.surface is None:
            return False

        lx = mouse_pos[0] - self.picker_state.pos[0]
        ly = mouse_pos[1] - self.picker_state.pos[1]
        if lx < 0 or ly < 0 or lx > self.picker_state.surface.get_width() or ly > self.picker_state.surface.get_height():
            return False

        # Coordenadas de la rejilla
        col = (lx - PAD) // (THUMB + PAD)
        row = (ly - PAD + self.editor_state.scroll_offset) // (THUMB + PAD)
        idx = row * COLS + col
        if not (0 <= col < COLS and row >= 0 and idx < len(self.assets)):
            return False

        value, _, is_dir = self.assets[idx]
        # Navegación de directorios
        if is_dir:
            if value == "..":
                # Subir
                self.current_dir = self.current_dir.parent
            else:
                # Entrar en subcarpeta
                self.current_dir = self.current_dir / value
            self._load_assets()
            return True

        # Selección de fichero
        self.editor_state.current_choice = value
        self.picker_state.current_choice = value

        # Arrastrar el 'tile picker'
        if button == 3:
            self.picker_state.dragging = True
            self.picker_state.drag_offset = (lx, ly)
            return True
        
        # Delete tile
        if self.picker_state.btn_delete_rect and self.picker_state.btn_delete_rect.collidepoint((lx, ly)):
            self._delete_tile(map)
            return True

        # Set default tile
        if self.picker_state.btn_default_rect and self.picker_state.btn_default_rect.collidepoint((lx, ly)):
            self._set_default(map)
            return True

        return True
    

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
            tile.sprite = pygame.Surface((TILE_SIZE, TILE_SIZE), pygame.SRCALPHA)
            tile.scaled_cache.clear()
            self._persist_overlay(tile, "", map)
        self._close()

    def _set_default(self, map):
        tile = self.editor_state.selected_tile
        if tile:
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
        row = tile.y // TILE_SIZE
        col = tile.x // TILE_SIZE
        if map.overlay is None:
            h = len(map.tiles)
            w = len(map.tiles[0]) if h else 0
            map.overlay = [["" for _ in range(w)] for _ in range(h)]
        map.overlay[row][col] = code
        save_overlay(map.name, map.overlay)
        print(f"📝 Overlay guardado en: {map.name}.overlay.json")
