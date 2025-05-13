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
        # Centrar el sprite en (model.x, model.y)
        sprite_w, sprite_h = scaled_sprite.get_size()
        draw_x = model.x - sprite_w // 2
        draw_y = model.y - sprite_h // 2
        screen.blit(scaled_sprite, camera.apply((draw_x, draw_y)))