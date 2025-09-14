import pygame
from roguelike_editors.particles.particles_controller import ParticlesEditorController

import logging
logger = logging.getLogger(__name__)

class ParticlesEditorManager:
    """
    Manager for the Particles Editor: builds controller and exposes its model.
    """
    def __init__(self, game):
        self.game = game
        font = getattr(game, 'font', None)
        self.controller = ParticlesEditorController(font)
        self.model = self.controller.model
        # Expose state globally if needed in future
        try:
            game.state.particles_editor_state = self.model
        except Exception:
            pass

    def handle_event(self, event: pygame.event.Event) -> None:
        self.controller.handle_event(event)

    def draw(self, screen: pygame.Surface) -> None:
        self.controller.draw(screen)
