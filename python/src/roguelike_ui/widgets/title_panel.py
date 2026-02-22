import pygame

class TitlePanel:
    """
    Generic title panel with semi-transparent background, border, and text.
    """
    def __init__(self, text, font, x, y,
                 bgcolor=(0, 0, 0, 180),
                 text_color=(255, 255, 255),
                 border_color=(255, 255, 255, 200),
                 border_width=2,
                 border_radius=6,
                 padding_x=12,
                 padding_y=8):
        self.text = text
        self.font = font
        self.x = x
        self.y = y
        self.bgcolor = bgcolor
        self.text_color = text_color
        self.border_color = border_color
        self.border_width = border_width
        self.border_radius = border_radius
        self.padding_x = padding_x
        self.padding_y = padding_y

    def render(self, screen):
        # Render title text
        text_surf = self.font.render(self.text or "", True, self.text_color)
        # Calculate background size
        bg_w = text_surf.get_width() + self.padding_x * 2
        bg_h = text_surf.get_height() + self.padding_y * 2
        # Create semi-transparent background
        bg_surf = pygame.Surface((bg_w, bg_h), pygame.SRCALPHA)
        bg_surf.fill(self.bgcolor)
        # Draw border with rounded corners
        pygame.draw.rect(
            bg_surf,
            self.border_color,
            bg_surf.get_rect(),
            self.border_width,
            border_radius=self.border_radius
        )
        # Blit background and text
        screen.blit(bg_surf, (self.x, self.y))
        screen.blit(text_surf, (self.x + self.padding_x, self.y + self.padding_y))
