# File: src.roguelike_project/systems/combat/spells/firework_launch/view.py
import pygame

class FireworkLaunchView:
    """
    Vista: renderiza el cohete y sus partículas.
    """
    def __init__(self, model):
        self.model = model

    def render(self, screen, camera):
        model = self.model
        # Renderizar estela
        for pd in model.particles:
            screen_pos = camera.apply((pd.x, pd.y))
            alpha = max(0, 255 * (1 - pd.age / pd.lifespan))
            surf = pygame.Surface((pd.size, pd.size), pygame.SRCALPHA)
            surf.fill((*pd.color, int(alpha)))
            screen.blit(surf, screen_pos)
        # Renderizar cohete actual (pequeño punto)
        if not model.finished:
            rocket_surf = pygame.Surface((4,4), pygame.SRCALPHA)
            rocket_surf.fill((255,255,255))
            screen.blit(rocket_surf, camera.apply((model.x, model.y)))