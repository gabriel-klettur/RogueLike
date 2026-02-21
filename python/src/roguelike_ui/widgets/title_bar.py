import pygame
from .title_panel import TitlePanel
from roguelike_ui.ui_blocker import register_blocker

class TitleBar:
    """
    Reusable title bar component built on top of TitlePanel.
    Renders a rounded translucent background with text and returns its rect for layout.
    """
    def __init__(self, text: str, x: int = 10, y: int = 10, font: pygame.font.Font | None = None):
        # Use a consistent default font if none provided
        self.font = font or pygame.font.SysFont("Arial", 24, bold=True)
        self.panel = TitlePanel(
            text=text,
            font=self.font,
            x=x,
            y=y,
        )

    @property
    def x(self) -> int:
        return self.panel.x

    @property
    def y(self) -> int:
        return self.panel.y

    def set_pos(self, x: int, y: int) -> None:
        self.panel.x = x
        self.panel.y = y

    def update_text(self, text: str) -> None:
        self.panel.text = text

    def render(self, screen: pygame.Surface) -> pygame.Rect:
        """
        Render the title and return its bounding rect for layout.
        """
        # Paint via TitlePanel
        self.panel.render(screen)
        # Compute background rect identical to TitlePanel's painted area
        text_surf = self.font.render(self.panel.text or "", True, self.panel.text_color)
        bg_w = text_surf.get_width() + self.panel.padding_x * 2
        bg_h = text_surf.get_height() + self.panel.padding_y * 2
        rect = pygame.Rect(self.panel.x, self.panel.y, bg_w, bg_h)
        # Register as UI blocker to suppress hover beneath the title bar
        register_blocker(rect)
        return rect
