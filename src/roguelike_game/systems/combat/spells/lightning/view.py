# Path: src/roguelike_game/systems/combat/spells/lightning/view.py
import pygame
import random

class LightningView:
    """
    Renderiza la línea poligonal del rayo con alpha dinámico.
    """
    def __init__(self, model):
        self.model = model

    def render(self, surface, camera):
        if self.model.is_finished():
            return None        

        # Crea superficie temporal transparente
        temp = pygame.Surface(surface.get_size(), pygame.SRCALPHA)
        alpha = int(255 * (self.model.lifetime / self.model.max_lifetime))

        # Color del rayo
        color = (random.randint(80,120), random.randint(180,230), 255, alpha)

        # Proyecta cada punto usando camera.apply
        pts = [camera.apply(pt) for pt in self.model.points]

        # Dibuja segmentos
        for a, b in zip(pts, pts[1:]):
            pygame.draw.line(temp, color, a, b, 2)

        # Blit sobre la superficie principal
        surface.blit(temp, (0,0))

        # Devuelve dirty rect (aprox. todo el screen)
        return surface.get_rect()