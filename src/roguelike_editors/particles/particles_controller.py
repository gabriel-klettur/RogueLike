import pygame
from .particles_model import ParticlesEditorModel
from .particles_view import ParticlesEditorView

class ParticlesEditorController:
    """Minimal controller for Particles Editor."""
    def __init__(self, font: pygame.font.Font | None = None):
        self.model = ParticlesEditorModel()
        self.view = ParticlesEditorView(self.model)
        self.font = font

    def toggle_visible(self):
        self.model.visible = not bool(self.model.visible)

    def handle_event(self, event: pygame.event.Event) -> None:
        # Placeholder for future interactions
        return

    def draw(self, screen: pygame.Surface) -> None:
        self.view.draw(screen)
