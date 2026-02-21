import pygame

class ScrollPanel:
    """
    Widget to display a vertical list of text items with mouse-wheel scrolling and scrollbar.
    """
    def __init__(self, font: pygame.font.Font, margin: int = 5, scroll_bar_width: int = 8):
        self.font = font
        self.margin = margin
        self.scroll_bar_width = scroll_bar_width
        self.items = []
        self.scroll_offset = 0
        self.rect = pygame.Rect(0, 0, 0, 0)

    def set_items(self, items: list):
        # Preserve scroll position; we'll clamp in draw when we know the viewport height
        self.items = items

    def handle_event(self, event: pygame.event.Event) -> bool:
        # Only handle wheel events within panel area
        if event.type == pygame.MOUSEBUTTONDOWN:
            if self.rect.collidepoint(event.pos):
                line_h = self.font.get_linesize()
                # wheel up
                if event.button == 4:
                    self.scroll_offset = max(self.scroll_offset - line_h, 0)
                    return True
                # wheel down
                if event.button == 5:
                    content_h = len(self.items) * line_h
                    avail = self.rect.height
                    max_off = max(content_h - avail, 0)
                    self.scroll_offset = min(self.scroll_offset + line_h, max_off)
                    return True
        # Handle newer mouse wheel events
        elif event.type == pygame.MOUSEWHEEL:
            # wheel y >0 up, <0 down
            if self.rect.collidepoint(pygame.mouse.get_pos()):
                line_h = self.font.get_linesize()
                if event.y > 0:
                    self.scroll_offset = max(self.scroll_offset - line_h, 0)
                elif event.y < 0:
                    content_h = len(self.items) * line_h
                    avail = self.rect.height
                    max_off = max(content_h - avail, 0)
                    self.scroll_offset = min(self.scroll_offset + line_h, max_off)
                return True
        return False

    def draw(self, surface: pygame.Surface, panel_rect: pygame.Rect):
        # store rect for event handling
        self.rect = panel_rect
        # clip to panel
        surface.set_clip(panel_rect)
        line_h = self.font.get_linesize()
        # Clamp scroll offset to content size to avoid overscroll when items change
        content_h = len(self.items) * line_h
        max_off = max(content_h - panel_rect.height, 0)
        if self.scroll_offset > max_off:
            self.scroll_offset = max_off
        y = panel_rect.y - self.scroll_offset
        # draw each line
        for line in self.items:
            surf = self.font.render(line, True, (255,255,255))
            surface.blit(surf, (panel_rect.x + self.margin, y))
            y += line_h
        # remove clip
        surface.set_clip(None)
        # draw scrollbar
        content_h = len(self.items) * line_h
        if content_h > panel_rect.height:
            bar_x = panel_rect.x + panel_rect.width - self.scroll_bar_width - self.margin
            bar_y = panel_rect.y
            bar_h = panel_rect.height
            # track
            track = pygame.Rect(bar_x, bar_y, self.scroll_bar_width, bar_h)
            pygame.draw.rect(surface, (80,80,80,150), track)
            # thumb size and position
            thumb_h = max(int(bar_h * (bar_h / content_h)), line_h)
            thumb_y = bar_y + int((self.scroll_offset / content_h) * bar_h)
            thumb = pygame.Rect(bar_x, thumb_y, self.scroll_bar_width, thumb_h)
            pygame.draw.rect(surface, (200,200,200,200), thumb)
