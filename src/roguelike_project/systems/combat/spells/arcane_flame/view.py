import pygame
from src.roguelike_project.systems.combat.spells.arcane_flame.model import ArcaneFlameModel, FirePixel

class ArcaneFlameView:
    """
    Vista del fuego arcano: delega en FirePixel.render().
    """
    def __init__(self, model: ArcaneFlameModel):
        self.model = model

    def render(self, screen: pygame.Surface, camera):
        if self.model.is_finished():
            return
        # Cada FirePixel sabe cómo dibujarse
        for row in self.model.pixels:
            for p in row:
                if p:
                    p.render(screen, camera)
