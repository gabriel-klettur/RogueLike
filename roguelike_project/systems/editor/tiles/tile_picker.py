import glob, pygame
from pathlib import Path
from roguelike_project.utils.loader import load_image
from roguelike_project.config import TILE_SIZE

THUMB = 56
COLS  = 6
PAD   = 6

CLR_BORDER     = (255, 255, 255)
CLR_HOVER      = (255, 230, 0)
CLR_SELECTION  = (255, 200, 0)

class TilePicker:
    """
    Ventana flotante de selección de tiles.
    – Mover con clic‑derecho y arrastre
    – Hover con borde amarillo
    – Selección actual con borde naranja
    """
    BTN_W = 100
    BTN_H = 28

    def __init__(self, state, editor_state):
        self.state   = state
        self.editor  = editor_state
        self.assets  = self._load_assets()
        self.font    = pygame.font.SysFont("Arial", 16)
        self.surface = None
        self.pos     = None   # se centra al abrir
        self.dragging    = False
        self.drag_offset = (0, 0)

        self.btn_delete_rect  = self.btn_default_rect = self.btn_accept_rect = None

    # ---------------------- assets ---------------------- #
    def _load_assets(self):
        root = Path(__file__).resolve().parents[3] / "assets" / "tiles"
        patterns = ["*.png", "*.PNG", "*.webp", "*.WEBP"]
        files = []
        for pat in patterns:
            files.extend(sorted(root.glob(pat)))

        thumbs = []
        for p in files:
            rel = str(Path("assets") / "tiles" / p.name)
            thumbs.append((rel, load_image(rel, (THUMB, THUMB))))
        return thumbs

    # ---------------------- render ---------------------- #
    def render(self, screen):
        if not self.editor.picker_open:
            return

        # -- dimensiones surface --
        w = COLS * (THUMB + PAD) + PAD
        rows = (len(self.assets) + COLS - 1) // COLS
        h_grid = rows * (THUMB + PAD) + PAD
        h = h_grid + PAD + self.BTN_H + PAD

        # -- crear/limpiar surface --
        if self.surface is None or self.surface.get_size() != (w, h):
            self.surface = pygame.Surface((w, h), pygame.SRCALPHA)
        self.surface.fill((20, 20, 20, 235))

        # -- grid de miniaturas --
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

            # borde hover / selección
            if rect.collidepoint((lx, ly)):
                pygame.draw.rect(self.surface, CLR_HOVER, rect, 3)
            elif self.editor.current_choice == path:
                pygame.draw.rect(self.surface, CLR_SELECTION, rect, 3)

        # -- botones --
        y_btn = h_grid + PAD
        self.btn_delete_rect  = pygame.Rect(PAD,                     y_btn, self.BTN_W, self.BTN_H)
        self.btn_default_rect = pygame.Rect(PAD*2 + self.BTN_W,      y_btn, self.BTN_W, self.BTN_H)
        self.btn_accept_rect  = pygame.Rect(PAD*3 + self.BTN_W*2,    y_btn, self.BTN_W, self.BTN_H)
        self._draw_button(self.btn_delete_rect,  "Borrar")
        self._draw_button(self.btn_default_rect, "Default")
        self._draw_button(self.btn_accept_rect,  "Aceptar")

        # -- posición --
        if self.pos is None:
            self.pos = ((screen.get_width()-w)//2, (screen.get_height()-h)//2)
        screen.blit(self.surface, self.pos)

    def _draw_button(self, rect, text):
        pygame.draw.rect(self.surface, (60, 60, 60), rect)
        pygame.draw.rect(self.surface, CLR_BORDER, rect, 1)
        txt = self.font.render(text, True, CLR_BORDER)
        self.surface.blit(txt, txt.get_rect(center=rect.center))

    # ------------------ interacción --------------------- #
    def handle_click(self, mouse_pos, button):
        if not self.editor.picker_open or self.surface is None:
            return False

        lx, ly = mouse_pos[0]-self.pos[0], mouse_pos[1]-self.pos[1]
        inside = 0 <= lx <= self.surface.get_width() and 0 <= ly <= self.surface.get_height()
        if not inside:
            return False

        if button == 3:      # drag
            self.dragging = True
            self.drag_offset = (lx, ly)
            return True

        # ----- botones/minis -----
        if self.btn_delete_rect .collidepoint((lx, ly)): self._delete_tile();  return True
        if self.btn_default_rect.collidepoint((lx, ly)): self._set_default();  return True
        if self.btn_accept_rect .collidepoint((lx, ly)): self._accept_choice();return True

        col = int((lx - PAD) // (THUMB + PAD))
        row = int((ly - PAD + self.editor.scroll_offset) // (THUMB + PAD))
        if 0 <= col < COLS and row >= 0:
            idx = row * COLS + col
            if 0 <= idx < len(self.assets):
                self.editor.current_choice = self.assets[idx][0]
        return True

    def drag(self, mouse_pos):
        if self.dragging:
            self.pos = (mouse_pos[0]-self.drag_offset[0],
                        mouse_pos[1]-self.drag_offset[1])

    def stop_drag(self):
        self.dragging = False

    # ------------- acciones botones ------------- #
    def _delete_tile(self):
        tile = self.editor.selected_tile
        if tile:
            tile.sprite = pygame.Surface((TILE_SIZE, TILE_SIZE), pygame.SRCALPHA)
            tile.scaled_cache.clear()
        self._close()

    def _set_default(self):
        from roguelike_project.engine.game.systems.map.tile_loader import load_tile_images
        tile = self.editor.selected_tile
        if tile:
            sprite_set = load_tile_images().get(tile.tile_type)
            sprite = sprite_set[0] if isinstance(sprite_set, list) else sprite_set
            tile.sprite = sprite
            tile.scaled_cache.clear()
        self._close()

    def _accept_choice(self):
        if self.editor.current_choice and self.editor.selected_tile:
            from roguelike_project.utils.loader import load_image
            self.editor.selected_tile.sprite = load_image(
                self.editor.current_choice, (TILE_SIZE, TILE_SIZE)
            )
            self.editor.selected_tile.scaled_cache.clear()
        self._close()

    def scroll(self, dy):
        self.editor.scroll_offset = max(0, self.editor.scroll_offset - dy*30)

    def _close(self):
        self.editor.picker_open = False
        self.editor.current_choice = None
        self.dragging = False
