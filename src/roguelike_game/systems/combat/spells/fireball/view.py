# Path: src/roguelike_game/systems/combat/spells/fireball/view.py
import pygame

class FireballView:
    """
    Dibuja el sprite del fireball si sigue vivo.
    """
    def __init__(self, model):
        self.model = model

    def render(self, screen, camera):
        model = self.model
        if not model.alive:
            return
        # Escalado según cámara
        scaled_sprite = pygame.transform.scale(
            model.sprite,
            camera.scale(model.size)
        )
        screen.blit(scaled_sprite, camera.apply((model.x, model.y)))