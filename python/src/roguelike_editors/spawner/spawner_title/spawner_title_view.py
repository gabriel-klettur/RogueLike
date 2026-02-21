import pygame
from roguelike_ui.widgets.title_bar import TitleBar

class SpawnerTitleView:
    """
    Vista para el panel de título del Spawner Editor.
    """
    def __init__(self, controller, model, font):
        self.controller = controller
        self.model = model
        # Consistent font/styling across editors
        self.font = pygame.font.SysFont("Arial", 24, bold=True)
        # Standard position
        self.x = 10
        self.y = 10
        # Reusable TitleBar component
        self.title_bar = TitleBar(text=self.model.title, x=self.x, y=self.y, font=self.font)
        # Expose inner widget if needed by layout systems
        self.widget = self.title_bar.panel

    def render(self, screen: pygame.Surface) -> pygame.Rect:
        self.title_bar.update_text(self.model.title)
        return self.title_bar.render(screen)
