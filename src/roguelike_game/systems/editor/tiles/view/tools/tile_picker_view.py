# roguelike_game/systems/editor/tiles/view/tools/tile_picker_view.py

# Path: src/roguelike_game/systems/editor/tiles/view/tools/tile_picker_view.py
import pygame
from roguelike_game.systems.editor.tiles.tiles_editor_config import CLR_HOVER, CLR_SELECTION, THUMB, COLS, PAD, CLR_BORDER, BTN_H, BTN_W

class TilePickerView:
    def __init__(self, picker_state, assets):        
        self.picker_state       = picker_state
        self.assets             = assets
        self.font = pygame.font.SysFont("Arial", 16)

    def render(self, screen):
        if not self.picker_state.open:
            return

        w = COLS * (THUMB + PAD) + PAD
        rows = (len(self.assets) + COLS - 1) // COLS
        h_grid = rows * (THUMB + PAD) + PAD
        h = h_grid + PAD + BTN_H + PAD

        if self.picker_state.surface is None or self.picker_state.surface.get_size() != (w, h):
            self.picker_state.surface = pygame.Surface((w, h), pygame.SRCALPHA)
        self.picker_state.surface.fill((20, 20, 20, 235))

        y0 = PAD - self.picker_state.scroll_offset
        mx, my = pygame.mouse.get_pos()
        lx = mx - (self.picker_state.pos[0] if self.picker_state.pos else 0)
        ly = my - (self.picker_state.pos[1] if self.picker_state.pos else 0)

        for idx, (path, thumb) in enumerate(self.assets):
            row, col = divmod(idx, COLS)
            x = PAD + col * (THUMB + PAD)
            y = y0 + row * (THUMB + PAD)
            rect = pygame.Rect(x, y, THUMB, THUMB)
            if rect.bottom < PAD or rect.top > h_grid:
                continue

            self.picker_state.surface.blit(thumb, rect)
            if rect.collidepoint((lx, ly)):
                pygame.draw.rect(self.picker_state.surface, CLR_HOVER, rect, 3)
            elif self.picker_state.current_choice == path:
                pygame.draw.rect(self.picker_state.surface, CLR_SELECTION, rect, 3)

        self.picker_state.btn_delete_rect  = pygame.Rect(PAD, PAD + h_grid, BTN_W, BTN_H)
        self.picker_state.btn_default_rect = pygame.Rect(PAD*2 + BTN_W, PAD + h_grid, BTN_W, BTN_H)
        self.picker_state.btn_accept_rect  = pygame.Rect(PAD*3 + BTN_W*2, PAD + h_grid, BTN_W, BTN_H)
        self._draw_button(self.picker_state.btn_delete_rect,  "Borrar")
        self._draw_button(self.picker_state.btn_default_rect, "Default")
        

        if self.picker_state.pos is None:
            sw, sh = screen.get_size()
            self.picker_state.pos = ((sw - w) // 2, (sh - h) // 2)
        screen.blit(self.picker_state.surface, self.picker_state.pos)

    def _draw_button(self, rect, text):
        pygame.draw.rect(self.picker_state.surface, (60, 60, 60), rect)
        pygame.draw.rect(self.picker_state.surface, CLR_BORDER, rect, 1)
        txt = self.font.render(text, True, CLR_BORDER)
        self.picker_state.surface.blit(txt, txt.get_rect(center=rect.center))