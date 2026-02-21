import pygame
from roguelike_ui.widgets.title_bar import TitleBar
from roguelike_ui.ui_blocker import register_blocker

class BuildingsTitleView:
    """Title view for the Buildings Editor using the reusable TitleBar."""
    def __init__(self, controller, state):
        self.controller = controller
        self.state = state
        initial_title = getattr(self.state, "title", "") or "BUILDINGS EDITOR"
        self.title_bar = TitleBar(text=initial_title, x=10, y=10)
        # Expose underlying TitlePanel for compatibility if needed
        self.widget = self.title_bar.panel

    def render(self, screen: pygame.Surface) -> pygame.Rect:
        current_title = getattr(self.state, "title", "") or "BUILDINGS EDITOR"
        self.title_bar.update_text(current_title)
        rect = self.title_bar.render(screen)
        try:
            register_blocker(rect)
        except Exception:
            pass
        return rect
