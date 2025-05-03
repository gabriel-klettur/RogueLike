# src/roguelike_game/entities/npc/base/view.py

import pygame
from ..base.interfaces import IView

class BaseNPCView(IView):
    """
    Vista base que dibuja una barra de salud simple encima del sprite.
    Subclases deben cargar `self.sprite` y definir `self.model`.
    """
    BAR_HEIGHT = 4

    def __init__(self, model, sprite: pygame.Surface):
        self.model = model
        self.sprite = sprite
        # asumimos que model tiene sprite_size
        self.size = getattr(model, "sprite_size", self.sprite.get_size())

    def render(self, screen, camera):
        # 1) dibujar sprite
        scaled = pygame.transform.scale(self.sprite, camera.scale(self.size))
        screen.blit(scaled, camera.apply((self.model.x, self.model.y)))

        # 2) dibujar barra de salud
        if hasattr(self.model, "health") and hasattr(self.model, "max_health"):
            ratio = max(0, self.model.health) / self.model.max_health
            w, h = camera.scale(self.size)
            bar_w = int(w * ratio)
            x, y = camera.apply((self.model.x, self.model.y - 10))
            pygame.draw.rect(screen, (255,0,0),   (x, y, w,   self.BAR_HEIGHT))
            pygame.draw.rect(screen, (0,255,0),   (x, y, bar_w, self.BAR_HEIGHT))
