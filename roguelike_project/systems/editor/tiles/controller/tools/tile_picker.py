# roguelike_project/systems/editor/tiles/tile_picker.py
import pygame
from pathlib import Path
from roguelike_project.utils.loader import load_image
from roguelike_project.config import TILE_SIZE
from roguelike_project.engine.game.systems.map.overlay_manager import save_overlay
from roguelike_project.config_tiles import INVERSE_OVERLAY_MAP

THUMB = 56
COLS  = 6
PAD   = 6

CLR_BORDER     = (255, 255, 255)
CLR_HOVER      = (255, 230, 0)
CLR_SELECTION  = (255, 200, 0)

class TilePicker:
    """
    Ventana flotante de selección de tiles.
    – Mover con clic‑derecho y arrastre
    – Hover con borde amarillo
    – Selección actual con borde naranja
    Cada cambio se guarda automáticamente en <map_name>.overlay.json
    """
    BTN_W = 100
    BTN_H = 28

    def __init__(self, state, editor_state):
        self.state   = state
        self.editor  = editor_state
        # Carga los assets con debug para verificar ruta y patrones
        self.assets  = self._load_assets()  # list of (path, Surface)
        self.font    = pygame.font.SysFont("Arial", 16)
        self.surface = None
        self.pos     = None
        self.dragging    = False
        self.drag_offset = (0, 0)

        # rects for buttons
        self.btn_delete_rect  = None
        self.btn_default_rect = None
        self.btn_accept_rect  = None

    def _load_assets(self):
        # Intentamos alcanzar la carpeta roguelike_project/assets/tiles
        # Ajustar parents según la profundidad real
        pkg_root = Path(__file__).resolve().parents[5]
        root = pkg_root / "assets" / "tiles"
        print(f"[TilePicker] pkg_root = {pkg_root!r}")
        print(f"[TilePicker] root = {root!r}")
        if not root.exists():
            print(f"[TilePicker] ⚠️ Carpeta no encontrada en: {root}")

        patterns = ["*.png", "*.PNG", "*.webp", "*.WEBP"]
        seen = {}
        for pat in patterns:
            found = list(root.glob(pat))
            print(f"[TilePicker] patrón {pat!r} → {len(found)} archivos")
            for path in found:
                key = path.name.lower()
                if key not in seen:
                    seen[key] = path

        files = sorted(seen.values())
        print(f"[TilePicker] archivos únicos encontrados: {len(files)} -> {[p.name for p in files]}")

        thumbs = []
        for p in files:
            rel = str(Path("assets") / "tiles" / p.name)
            try:
                surf = load_image(rel, (THUMB, THUMB))
            except Exception as e:
                print(f"[TilePicker] ERROR cargando {rel!r}: {e}")
                continue
            thumbs.append((rel, surf))

        print(f"[TilePicker] miniaturas cargadas: {len(thumbs)}")
        return thumbs

    def open_with_selection(self, choice_path):
        self.editor.picker_open = True
        self.editor.current_choice = choice_path
        for idx, (path, _) in enumerate(self.assets):
            if path == choice_path:
                row = idx // COLS
                self.editor.scroll_offset = row * (THUMB + PAD)
                break

    def is_over(self, mouse_pos) -> bool:
        if not self.surface or not self.pos:
            return False
        x0, y0 = self.pos
        w, h = self.surface.get_size()
        mx, my = mouse_pos
        return x0 <= mx <= x0 + w and y0 <= my <= y0 + h

    def render(self, screen):
        if not self.editor.picker_open:
            return

        w = COLS * (THUMB + PAD) + PAD
        rows = (len(self.assets) + COLS - 1) // COLS
        h_grid = rows * (THUMB + PAD) + PAD
        h = h_grid + PAD + self.BTN_H + PAD

        if self.surface is None or self.surface.get_size() != (w, h):
            self.surface = pygame.Surface((w, h), pygame.SRCALPHA)
        self.surface.fill((20, 20, 20, 235))

        y0 = PAD - self.editor.scroll_offset
        mx, my = pygame.mouse.get_pos()
        lx = mx - (self.pos[0] if self.pos else 0)
        ly = my - (self.pos[1] if self.pos else 0)

        for idx, (path, thumb) in enumerate(self.assets):
            row, col = divmod(idx, COLS)
            x = PAD + col * (THUMB + PAD)
            y = y0  + row * (THUMB + PAD)
            rect = pygame.Rect(x, y, THUMB, THUMB)
            if rect.bottom < PAD or rect.top > h_grid:
                continue

            self.surface.blit(thumb, rect)
            if rect.collidepoint((lx, ly)):
                pygame.draw.rect(self.surface, CLR_HOVER, rect, 3)
            elif self.editor.current_choice == path:
                pygame.draw.rect(self.surface, CLR_SELECTION, rect, 3)

        y_btn = h_grid + PAD
        self.btn_delete_rect  = pygame.Rect(PAD, PAD + h_grid, self.BTN_W, self.BTN_H)
        self.btn_default_rect = pygame.Rect(PAD*2 + self.BTN_W, PAD + h_grid, self.BTN_W, self.BTN_H)
        self.btn_accept_rect  = pygame.Rect(PAD*3 + self.BTN_W*2, PAD + h_grid, self.BTN_W, self.BTN_H)
        self._draw_button(self.btn_delete_rect,  "Borrar")
        self._draw_button(self.btn_default_rect, "Default")
        self._draw_button(self.btn_accept_rect,  "Aceptar")

        if self.pos is None:
            sw, sh = screen.get_size()
            self.pos = ((sw - w) // 2, (sh - h) // 2)
        screen.blit(self.surface, self.pos)

    def _draw_button(self, rect, text):
        pygame.draw.rect(self.surface, (60, 60, 60), rect)
        pygame.draw.rect(self.surface, CLR_BORDER, rect, 1)
        txt = self.font.render(text, True, CLR_BORDER)
        self.surface.blit(txt, txt.get_rect(center=rect.center))

    def handle_click(self, mouse_pos, button):
        if not self.editor.picker_open or self.surface is None:
            return False

        lx = mouse_pos[0] - self.pos[0]
        ly = mouse_pos[1] - self.pos[1]
        if lx < 0 or ly < 0 or lx > self.surface.get_width() or ly > self.surface.get_height():
            return False

        if button == 3:
            self.dragging = True
            self.drag_offset = (lx, ly)
            return True

        if self.btn_delete_rect.collidepoint((lx, ly)):
            self._delete_tile()
            return True
        if self.btn_default_rect.collidepoint((lx, ly)):
            self._set_default()
            return True
        if self.btn_accept_rect.collidepoint((lx, ly)):
            self._accept_choice()
            return True

        col = (lx - PAD) // (THUMB + PAD)
        row = (ly - PAD + self.editor.scroll_offset) // (THUMB + PAD)
        idx = row * COLS + col
        if 0 <= col < COLS and 0 <= row and idx < len(self.assets):
            self.editor.current_choice = self.assets[idx][0]
        return True

    def drag(self, mouse_pos):
        if self.dragging:
            self.pos = (
                mouse_pos[0] - self.drag_offset[0],
                mouse_pos[1] - self.drag_offset[1]
            )

    def stop_drag(self):
        self.dragging = False

    def _delete_tile(self):
        tile = self.editor.selected_tile
        if tile:
            tile.sprite = pygame.Surface((TILE_SIZE, TILE_SIZE), pygame.SRCALPHA)
            tile.scaled_cache.clear()
            self._persistir_overlay(tile, "")
        self._close()

    def _set_default(self):
        from roguelike_project.engine.game.systems.map.tile_loader import load_tile_images
        tile = self.editor.selected_tile
        if tile:
            imgs = load_tile_images().get(tile.tile_type)
            sprite = imgs[0] if isinstance(imgs, list) else imgs
            tile.sprite = sprite
            tile.scaled_cache.clear()
            self._persistir_overlay(tile, "")
        self._close()

    def _accept_choice(self):
        choice = self.editor.current_choice
        tile   = self.editor.selected_tile
        if choice and tile:
            tile.sprite = load_image(choice, (TILE_SIZE, TILE_SIZE))
            tile.scaled_cache.clear()
            name = Path(choice).stem
            code = (INVERSE_OVERLAY_MAP.get(name) or [""])[0]
            self._persistir_overlay(tile, code)
        self._close()

    def scroll(self, dy):
        self.editor.scroll_offset = max(0, self.editor.scroll_offset - dy * 30)

    def _close(self):
        self.editor.picker_open   = False
        self.editor.current_choice = None
        self.dragging = False

    def _persistir_overlay(self, tile, code: str):
        row = tile.y // TILE_SIZE
        col = tile.x // TILE_SIZE
        if self.state.overlay_map is None:
            h = len(self.state.tile_map)
            w = len(self.state.tile_map[0]) if h else 0
            self.state.overlay_map = [["" for _ in range(w)] for _ in range(h)]
        self.state.overlay_map[row][col] = code
        save_overlay(self.state.map_name, self.state.overlay_map)
        print(f"📝 Overlay guardado en: {self.state.map_name}.overlay.json")
