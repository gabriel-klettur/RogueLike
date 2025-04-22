import pygame

from roguelike_project.systems.editor.tiles.tiles_editor_config import CLR_HOVER, CLR_SELECTION, THUMB, COLS, PAD

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