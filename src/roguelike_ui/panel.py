import pygame

class PanelSurface:
    """Manage panel surface init/resize and background fill."""
    def __init__(self, width, height, bgcolor=(20, 20, 20, 235)):
        self.bgcolor = bgcolor
        self.surface = pygame.Surface((width, height), pygame.SRCALPHA)
        self.surface.fill(self.bgcolor)

    def resize(self, width, height):
        """Resize surface if dimensions change and refill background."""
        if self.surface.get_size() != (width, height):
            self.surface = pygame.Surface((width, height), pygame.SRCALPHA)
        self.surface.fill(self.bgcolor)

class DraggablePanel(PanelSurface):
    """Add draggable behavior to PanelSurface."""
    def __init__(self, width, height, bgcolor=(20, 20, 20, 235)):
        super().__init__(width, height, bgcolor)
        self.pos = None  # (x, y)
        self.dragging = False
        self.drag_offset = (0, 0)

    def handle_event(self, event, header_rect=None):
        """
        Handle pygame events to enable dragging.
        event: pygame event
        header_rect: pygame.Rect defining draggable area
        """
        if event.type == pygame.MOUSEBUTTONDOWN and header_rect and header_rect.collidepoint(event.pos):
            self.dragging = True
            mx, my = event.pos
            ox = mx - (self.pos[0] if self.pos else 0)
            oy = my - (self.pos[1] if self.pos else 0)
            self.drag_offset = (ox, oy)
        elif event.type == pygame.MOUSEBUTTONUP and self.dragging:
            self.dragging = False
        elif event.type == pygame.MOUSEMOTION and self.dragging:
            mx, my = event.pos
            dx, dy = self.drag_offset
            self.pos = (mx - dx, my - dy)
