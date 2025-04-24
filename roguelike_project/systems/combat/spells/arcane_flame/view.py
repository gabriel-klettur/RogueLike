import pygame
from roguelike_project.systems.combat.spells.arcane_flame.model import ArcaneFlameModel, FirePixel

class ArcaneFlameView:
    """
    Vista del fuego arcano: delega en FirePixel.render().
    """
    def __init__(self, model: ArcaneFlameModel):
        self.m = model

    def render(self, screen: pygame.Surface, camera):
        if self.m.is_finished():
            return
        # Cada FirePixel sabe cómo dibujarse
        for row in self.m.pixels:
            for p in row:
                if p:
                    p.render(screen, camera)
