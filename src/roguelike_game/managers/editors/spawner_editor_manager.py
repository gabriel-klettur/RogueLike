import pygame
from roguelike_editors.spawner.spawner_editor_controller import SpawnerEditorController


class SpawnerEditorManager:
    """
    Manager for the Spawner Editor. Wraps controller and exposes a simple API
    like other editors.
    """
    def __init__(self, game):
        self.game = game
        font = getattr(game, 'font', None)
        self.controller = SpawnerEditorController(font)
        self.controller.set_game(game)
        # Expose model for external visibility checks
        self.model = self.controller.model

    def handle_event(self, event: pygame.event.Event) -> bool:
        return self.controller.handle_event(event)

    def draw(self, screen: pygame.Surface) -> None:
        self.controller.render(screen)
