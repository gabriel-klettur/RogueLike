# roguelike_game/systems/editor/tiles/view/tools/tile_picker_view.py

# Path: src/roguelike_game/systems/editor/tiles/view/tools/tile_picker_view.py
import pygame
from roguelike_game.systems.editor.tiles.tiles_editor_config import CLR_HOVER, CLR_SELECTION, THUMB, COLS, PAD

class TilePickerView:
    def __init__(self, picker):
        self.picker = picker

    def render(self, screen):
        if not self.picker.editor.picker_open:
            return

        w = COLS * (THUMB + PAD) + PAD
        rows = (len(self.picker.assets) + COLS - 1) // COLS
        h_grid = rows * (THUMB + PAD) + PAD
        h = h_grid + PAD + self.picker.BTN_H + PAD

        if self.picker.surface is None or self.picker.surface.get_size() != (w, h):
            self.picker.surface = pygame.Surface((w, h), pygame.SRCALPHA)
        self.picker.surface.fill((20, 20, 20, 235))

        y0 = PAD - self.picker.editor.scroll_offset
        mx, my = pygame.mouse.get_pos()
        lx = mx - (self.picker.pos[0] if self.picker.pos else 0)
        ly = my - (self.picker.pos[1] if self.picker.pos else 0)

        for idx, (path, thumb) in enumerate(self.picker.assets):
            row, col = divmod(idx, COLS)
            x = PAD + col * (THUMB + PAD)
            y = y0 + row * (THUMB + PAD)
            rect = pygame.Rect(x, y, THUMB, THUMB)
            if rect.bottom < PAD or rect.top > h_grid:
                continue

            self.picker.surface.blit(thumb, rect)
            if rect.collidepoint((lx, ly)):
                pygame.draw.rect(self.picker.surface, CLR_HOVER, rect, 3)
            elif self.picker.editor.current_choice == path:
                pygame.draw.rect(self.picker.surface, CLR_SELECTION, rect, 3)

        self.picker.btn_delete_rect  = pygame.Rect(PAD, PAD + h_grid, self.picker.BTN_W, self.picker.BTN_H)
        self.picker.btn_default_rect = pygame.Rect(PAD*2 + self.picker.BTN_W, PAD + h_grid, self.picker.BTN_W, self.picker.BTN_H)
        self.picker.btn_accept_rect  = pygame.Rect(PAD*3 + self.picker.BTN_W*2, PAD + h_grid, self.picker.BTN_W, self.picker.BTN_H)
        self.picker._draw_button(self.picker.btn_delete_rect,  "Borrar")
        self.picker._draw_button(self.picker.btn_default_rect, "Default")
        self.picker._draw_button(self.picker.btn_accept_rect,  "Aceptar")

        if self.picker.pos is None:
            sw, sh = screen.get_size()
            self.picker.pos = ((sw - w) // 2, (sh - h) // 2)
        screen.blit(self.picker.surface, self.picker.pos)