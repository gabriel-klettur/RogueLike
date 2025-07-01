import pygame
from roguelike_game.ecs.systems.rendering.combat.spells.firework_launch.model import FireworkLaunchModel, ParticleData

class FireworkLaunchView:
    """
    Vista: renderiza el cohete y sus partículas.
    """
    def __init__(self, model: FireworkLaunchModel):
        self.model = model

    def render(self, screen: pygame.Surface, camera):
        # Renderizar estela de partículas
        for pd in self.model.particles:
            screen_pos = camera.apply((pd.x, pd.y))
            alpha = max(0, 255 * (1 - pd.age / pd.lifespan))
            surf = pygame.Surface((pd.size, pd.size), pygame.SRCALPHA)
            surf.fill((*pd.color, int(alpha)))
            screen.blit(surf, screen_pos)
        # Renderizar cohete actual (pequeño punto)
        if not self.model.finished:
            rocket_surf = pygame.Surface((4,4), pygame.SRCALPHA)
            rocket_surf.fill((255,255,255))
            screen.blit(rocket_surf, camera.apply((self.model.x, self.model.y)))
