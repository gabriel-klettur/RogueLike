import pygame
from .particles_model import ParticlesEditorModel
from roguelike_editors.particles.particles_title_panel.particles_title_view import ParticlesTitleView

class ParticlesEditorView:
    """Minimal view for the Particles Editor. Renders the title bar."""
    def __init__(self, model: ParticlesEditorModel):
        self.model = model
        self.title_view = ParticlesTitleView(None, model)
        self.title_rect: pygame.Rect | None = None

    def draw(self, screen: pygame.Surface) -> None:
        if not self.model.visible:
            return
        rect = self.title_view.render(screen)
        self.model.title_rect = rect
        self.title_rect = rect
