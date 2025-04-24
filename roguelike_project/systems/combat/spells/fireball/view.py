# roguelike_project/systems/combat/spells/fireball/view.py
import pygame

class FireballView:
    """
    Dibuja el sprite del fireball si sigue vivo.
    """
    def __init__(self, model):
        self.m = model

    def render(self, screen, camera):
        m = self.m
        if not m.alive:
            return
        # Escalado según cámara
        scaled_sprite = pygame.transform.scale(
            m.sprite,
            camera.scale(m.size)
        )
        screen.blit(scaled_sprite, camera.apply((m.x, m.y)))
