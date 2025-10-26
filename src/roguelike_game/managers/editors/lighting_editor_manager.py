import pygame
from roguelike_editors.lighting.lighting_controller import LightingEditorController


class LightingEditorManager:
    """Manager for the Lighting Editor: builds controller and exposes its model."""
    def __init__(self, game):
        self.game = game
        font = getattr(game, 'font', None)
        self.controller = LightingEditorController(font)
        # Provide game reference to controller for camera/world conversions
        try:
            self.controller.game = game
        except Exception:
            pass
        self.model = self.controller.model

    def handle_event(self, event: pygame.event.Event) -> None:
        self.controller.handle_event(event)

    def draw(self, screen: pygame.Surface) -> None:
        self.controller.render(screen)
