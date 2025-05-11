
# Path: src/roguelike_game/systems/editor/tiles/controller/tools/tile_picker.py
import pygame
from pathlib import Path

from roguelike_engine.utils.loader import load_image
from roguelike_engine.config_tiles import TILE_SIZE, INVERSE_OVERLAY_MAP
from roguelike_engine.config import ASSETS_DIR
from roguelike_engine.map.overlay.overlay_manager import save_overlay
from roguelike_engine.tiles.assets import load_base_tile_images

from roguelike_game.systems.editor.tiles.tiles_editor_config import PAD, THUMB, COLS


class TilePickerController:
    """
    Ventana flotante de selección de tiles.
    – Mover con clic-derecho y arrastre
    – Hover con borde amarillo
    – Selección actual con borde naranja
    Cada cambio se guarda automáticamente en <map_name>.overlay.json
    """


    def __init__(self, state, editor_state, picker_state):
        self.state   = state
        self.editor_state  = editor_state
        self.picker_state = picker_state

        # Carga los assets desde la carpeta global de assets/tiles
        self.assets  = self._load_assets()  # list of (rel_path, Surface)
        #self.font    = pygame.font.SysFont("Arial", 16)
                
        #self.dragging    = False
        #self.drag_offset = (0, 0)

        #self.surface = None
        
        #self.picker_state.btn_delete_rect  = None
        #self.picker_state.btn_default_rect = None
        #self.picker_state.btn_accept_rect  = None

    def _load_assets(self):
        """
        Escanea la carpeta assets/tiles para cargar miniaturas.
        """
        tiles_root = Path(ASSETS_DIR) / "tiles"
        if not tiles_root.is_dir():
            print(f"[TilePicker] Carpeta no encontrada: {tiles_root}")
            return []

        patterns = ["*.png", "*.PNG", "*.webp", "*.WEBP"]
        seen = {}
        for pat in patterns:
            for path in tiles_root.glob(pat):
                key = path.name.lower()
                if key not in seen:
                    seen[key] = path
        files = sorted(seen.values())

        thumbs = []
        for p in files:
            rel = str(Path("tiles") / p.name)
            try:
                surf = load_image(rel, (THUMB, THUMB))
            except Exception as e:
                print(f"[TilePicker] ERROR cargando {rel!r}: {e}")
                continue
            thumbs.append((rel, surf))

        return thumbs

    def open_with_selection(self, choice_path):
        self.picker_state.open = True
        self.picker_state.current_choice = choice_path
        for idx, (path, _) in enumerate(self.assets):
            if path == choice_path:
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

        # Mover ventana
        if button == 3:
            self.picker_state.dragging = True
            self.picker_state.drag_offset = (lx, ly)
            return True

        # Botones
        if self.picker_state.btn_delete_rect and self.picker_state.btn_delete_rect.collidepoint((lx, ly)):
            self._delete_tile(map)
            return True
        if self.picker_state.btn_default_rect and self.picker_state.btn_default_rect.collidepoint((lx, ly)):
            self._set_default(map)
            return True

        # Selección de miniatura
        col = (lx - PAD) // (THUMB + PAD)
        row = (ly - PAD + self.editor_state.scroll_offset) // (THUMB + PAD)
        idx = row * COLS + col
        if 0 <= col < COLS and row >= 0 and idx < len(self.assets):
            self.editor_state.current_choice = self.assets[idx][0]
            return True

        return False

    def drag(self, mouse_pos):
        if self.picker_state.dragging:
            self.picker_state.pos = (
                mouse_pos[0] - self.picker_state.drag_offset[0],
                mouse_pos[1] - self.picker_state.drag_offset[1]
            )

    def stop_drag(self):
        self.picker_state.dragging = False

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


    def scroll(self, dy):
        self.editor_state.scroll_offset = max(0, self.editor_state.scroll_offset - dy * 30)

    def _close(self):
        self.editor_state.picker_open    = False
        self.editor_state.current_choice = None
        self.picker_state.dragging       = False

    def _persist_overlay(self, tile, code: str, map):
        row = tile.y // TILE_SIZE
        col = tile.x // TILE_SIZE
        if map.overlay is None:
            h = len(map.tiles)
            w = len(map.tiles[0]) if h else 0
            self.state.overlay_map = [["" for _ in range(w)] for _ in range(h)]
        map.overlay[row][col] = code
        save_overlay(map.name, map.overlay)
        print(f"📝 Overlay guardado en: {map.name}.overlay.json")