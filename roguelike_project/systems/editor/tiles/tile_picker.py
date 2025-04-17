import os, glob, pygame
from roguelike_project.utils.loader import load_image
from roguelike_project.config import TILE_SIZE

THUMB = 56           # tamaño de miniatura
COLS  = 6

class TilePicker:
    """
    Ventana flotante con todos los assets de assets/tiles/.
    """
    PAD = 6
    BTN_W, BTN_H = 100, 28

    def __init__(self, state, editor_state):
        self.state  = state
        self.editor = editor_state
        self.assets = self._load_assets()
        self.font   = pygame.font.SysFont("Arial", 16)
        self.surface = None             # se crea en render

        # botones
        self.btn_delete_rect = self.btn_default_rect = self.btn_accept_rect = None

    # ---------------------------------------------------- #
    def _load_assets(self):
        paths = sorted(glob.glob("assets/tiles/*.png"))
        thumbs = [
            (p, load_image(p, (THUMB, THUMB)))
            for p in paths
        ]
        return thumbs

    # ---------------------------------------------------- #
    # RENDER
    def render(self, screen):
        if not self.editor.picker_open:
            return

        w = COLS * (THUMB + self.PAD) + self.PAD
        rows = (len(self.assets) + COLS - 1) // COLS
        h_grid = rows * (THUMB + self.PAD) + self.PAD
        h = h_grid + 3 * (self.BTN_H + self.PAD) + self.PAD

        if self.surface is None or self.surface.get_size() != (w, h):
            self.surface = pygame.Surface((w, h), pygame.SRCALPHA)

        self.surface.fill((20, 20, 20, 235))

        # --- grid ---
        y0 = self.PAD - self.editor.scroll_offset
        for idx, (path, thumb) in enumerate(self.assets):
            row, col = divmod(idx, COLS)
            x = self.PAD + col * (THUMB + self.PAD)
            y = y0 + row * (THUMB + self.PAD)
            rect = pygame.Rect(x, y, THUMB, THUMB)
            if rect.bottom < self.PAD or rect.top > h_grid:      # recorte vertical
                continue
            self.surface.blit(thumb, rect)
            if self.editor.current_choice == path:
                pygame.draw.rect(self.surface, (255, 200, 0), rect, 3)

        # --- botones ---
        y_btn = h_grid + self.PAD
        self.btn_delete_rect  = pygame.Rect(self.PAD, y_btn, self.BTN_W, self.BTN_H)
        self.btn_default_rect = pygame.Rect(self.PAD*2 + self.BTN_W, y_btn, self.BTN_W, self.BTN_H)
        self.btn_accept_rect  = pygame.Rect(self.PAD*3 + self.BTN_W*2, y_btn, self.BTN_W, self.BTN_H)

        self._draw_button(self.btn_delete_rect,  "Borrar")
        self._draw_button(self.btn_default_rect, "Default")
        self._draw_button(self.btn_accept_rect,  "Aceptar")

        # --- blit global ---
        cx = (screen.get_width() - w) // 2
        cy = (screen.get_height() - h) // 2
        screen.blit(self.surface, (cx, cy))

    # ---------------------------------------------------- #
    def _draw_button(self, rect, text):
        pygame.draw.rect(self.surface, (60, 60, 60), rect)
        pygame.draw.rect(self.surface, (255, 255, 255), rect, 1)
        txt = self.font.render(text, True, (255, 255, 255))
        self.surface.blit(txt, txt.get_rect(center=rect.center))

    # ---------------------------------------------------- #
    # INTERACCIÓN
    def handle_click(self, mouse_pos):
        if not self.editor.picker_open:
            return False

        # coordenadas locales en la surface
        cx = (self.state.screen.get_width()  - self.surface.get_width())  // 2
        cy = (self.state.screen.get_height() - self.surface.get_height()) // 2
        lx, ly = mouse_pos[0] - cx, mouse_pos[1] - cy

        # botones
        if self.btn_delete_rect.collidepoint((lx, ly)):
            self._delete_tile()
            return True
        if self.btn_default_rect.collidepoint((lx, ly)):
            self._set_default()
            return True
        if self.btn_accept_rect.collidepoint((lx, ly)):
            self._accept_choice()
            return True

        # clic en miniaturas
        row = int((ly - self.PAD + self.editor.scroll_offset) // (THUMB + self.PAD))
        col = int((lx - self.PAD) // (THUMB + self.PAD))
        if 0 <= col < COLS and row >= 0:
            idx = row * COLS + col
            if 0 <= idx < len(self.assets):
                self.editor.current_choice = self.assets[idx][0]
        return True

    def _delete_tile(self):
        tile = self.editor.selected_tile
        if tile:
            tile.sprite = pygame.Surface((TILE_SIZE, TILE_SIZE), pygame.SRCALPHA)
        self._close()

    def _set_default(self):
        from roguelike_project.engine.game.systems.map.tile_loader import load_tile_images
        types = load_tile_images()
        tile = self.editor.selected_tile
        if tile:
            sprite = types.get(tile.tile_type)
            if isinstance(sprite, list):
                sprite = sprite[0]
            tile.sprite = sprite
        self._close()

    def _accept_choice(self):
        if self.editor.current_choice and self.editor.selected_tile:
            from roguelike_project.utils.loader import load_image
            self.editor.selected_tile.sprite = load_image(
                self.editor.current_choice, (TILE_SIZE, TILE_SIZE)
            )
        self._close()

    def scroll(self, dy):
        self.editor.scroll_offset = max(0, self.editor.scroll_offset - dy*30)

    def _close(self):
        self.editor.picker_open = False
        self.editor.current_choice = None
