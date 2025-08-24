import pygame
from roguelike_game.ecs.components.physics.mask_collider import MaskCollider
from roguelike_game.ecs.components.physics.circle_collider import CircleCollider
from roguelike_game.ecs.components.physics.multi_collider import MultiCollider
from roguelike_game.factories.player.config import FEET_WIDTH_DIVISOR, FEET_HEIGHT_DIVISOR

def create_body_and_feet(sprite_surface: pygame.Surface) -> MultiCollider:
    """
    Genera un MultiCollider que contiene:
      - "body": MaskCollider basado en la máscara de píxeles opacos del sprite.
      - "feet": CircleCollider para movimiento/colisión suave.
    """
    mask = pygame.mask.from_surface(sprite_surface)
    body = MaskCollider(mask, offset_x=0, offset_y=0)
    w, h = sprite_surface.get_size()
    feet_w = max(2, w // FEET_WIDTH_DIVISOR)
    feet_h = max(2, h // FEET_HEIGHT_DIVISOR)
    # Radio como la mitad del menor tamaño propuesto para el rect previo
    radius = max(4, min(feet_w, feet_h) // 2)
    # Centro en bottom-center del sprite, apoyado en la base
    center_x = w // 2
    center_y = h - radius - 1
    feet = CircleCollider(radius=radius, offset_x=center_x, offset_y=center_y)
    return MultiCollider({"body": body, "feet": feet})