import pygame

class Button:
    """Generic button with hover overlay and border."""
    def __init__(self, rect, bgcolor=(60,60,60), border_color=(255,255,255), hover_color=(255,255,0,100), border_width=1):
        self.rect = pygame.Rect(rect)
        self.bgcolor = bgcolor
        self.border_color = border_color
        self.hover_color = hover_color
        self.border_width = border_width
        self.hover = False

    def is_hovered(self, mouse_pos):
        """Update and return hover state based on mouse_pos."""
        self.hover = self.rect.collidepoint(mouse_pos)
        return self.hover

    def draw(self, surface):
        """Draw background, hover overlay, and border."""
        pygame.draw.rect(surface, self.bgcolor, self.rect)
        if self.hover:
            hover_surf = pygame.Surface((self.rect.width, self.rect.height), pygame.SRCALPHA)
            hover_surf.fill(self.hover_color)
            surface.blit(hover_surf, self.rect.topleft)
        pygame.draw.rect(surface, self.border_color, self.rect, self.border_width)

    def render_icon_x(self, surface, color=None, thickness=2, margin=4):
        """Draw an X icon inside the button rect."""
        col = color or self.border_color
        left, top, width, height = self.rect
        start1 = (left + margin, top + margin)
        end1 = (left + width - margin, top + height - margin)
        start2 = (left + width - margin, top + margin)
        end2 = (left + margin, top + height - margin)
        pygame.draw.line(surface, col, start1, end1, thickness)
        pygame.draw.line(surface, col, start2, end2, thickness)
