import pygame
from roguelike_ui.widgets.title_bar import TitleBar

class TilesTilesView:
    """View for the Tiles Title Panel"""
    def __init__(self, controller, state):
        self.controller = controller
        self.state = state
        # Reusable TitleBar (contains TitlePanel internally)
        initial_title = getattr(self.state, "title", "") or "TILES EDITOR"
        self.title_bar = TitleBar(text=initial_title, x=10, y=10)
        # Keep compatibility handle to the underlying TitlePanel
        self.widget = self.title_bar.panel

    def render(self, screen: pygame.Surface) -> pygame.Rect:
        # Update dynamic text from state and render via TitleBar
        current_title = getattr(self.state, "title", "") or "TILES EDITOR"
        self.title_bar.update_text(current_title)
        return self.title_bar.render(screen)
