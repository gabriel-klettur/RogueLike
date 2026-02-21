import pygame

class GridLayout:
    """Compute panel layout for a grid of items."""
    def __init__(self, thumb_size, pad, items_count, cols=None):
        self.thumb_size = thumb_size
        self.pad = pad
        self.items_count = items_count
        self.cols = cols or int(items_count**0.5) or 1
        self.rows = (items_count + self.cols - 1) // self.cols

    def compute(self):
        """Return cols, rows, total width, grid height."""
        w = self.cols * (self.thumb_size + self.pad) + self.pad
        h_grid = self.rows * (self.thumb_size + self.pad) + self.pad
        return self.cols, self.rows, w, h_grid

class ScrollableGrid(GridLayout):
    """Grid with vertical scroll support and clipping."""
    def __init__(self, thumb_size, pad, items_count, scroll_offset=0, cols=None):
        super().__init__(thumb_size, pad, items_count, cols)
        self.scroll_offset = scroll_offset

    def draw_items(self, surface, items, panel_pos, draw_fn):
        """
        Draw items using draw_fn and return hovered item.
        panel_pos: (x,y) offset of the panel surface on screen
        draw_fn(surface, rect, value, index) -> None
        """
        hovered = None
        mx, my = pygame.mouse.get_pos()
        lx, ly = mx - (panel_pos[0] or 0), my - (panel_pos[1] or 0)
        for idx, value in enumerate(items):
            row, col = divmod(idx, self.cols)
            x = self.pad + col * (self.thumb_size + self.pad)
            y = self.pad - self.scroll_offset + row * (self.thumb_size + self.pad)
            rect = pygame.Rect(x, y, self.thumb_size, self.thumb_size)
            # skip items outside viewport
            _, _, _, h_grid = self.compute()
            if rect.bottom < self.pad or rect.top > h_grid:
                continue
            draw_fn(surface, rect, value, idx)
            if rect.collidepoint((lx, ly)):
                hovered = value
        return hovered
