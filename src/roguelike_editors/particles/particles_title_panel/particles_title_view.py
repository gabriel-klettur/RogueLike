import pygame
from roguelike_ui.widgets.title_bar import TitleBar

class ParticlesTitleView:
    """Title view for the Particles Editor using the reusable TitleBar."""
    def __init__(self, controller=None, state=None):
        self.controller = controller
        self.state = state
        initial_title = getattr(self.state, "title", "") or "PARTICLES EDITOR"
        self.title_bar = TitleBar(text=initial_title, x=10, y=10)
        # Expose underlying TitlePanel for compatibility
        self.widget = self.title_bar.panel

    def render(self, screen: pygame.Surface) -> pygame.Rect:
        current_title = getattr(self.state, "title", "") or "PARTICLES EDITOR"
        self.title_bar.update_text(current_title)
        return self.title_bar.render(screen)
