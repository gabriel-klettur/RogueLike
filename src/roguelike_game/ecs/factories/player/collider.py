import pygame
from roguelike_game.ecs.components.physics.mask_collider import MaskCollider
from roguelike_game.ecs.components.physics.collider import Collider
from roguelike_game.ecs.components.physics.multi_collider import MultiCollider
from roguelike_game.ecs.factories.player.config import FEET_WIDTH_DIVISOR, FEET_HEIGHT_DIVISOR

def create_body_and_feet(sprite_surface: pygame.Surface) -> MultiCollider:
    """
    Genera un MultiCollider que contiene:
      - "body": MaskCollider basado en la máscara de píxeles opacos del sprite.
      - "feet": Collider rectangular en la parte inferior del sprite.
    """
    mask = pygame.mask.from_surface(sprite_surface)
    body = MaskCollider(mask, offset_x=0, offset_y=0)
    w, h = sprite_surface.get_size()
    feet_w = w // FEET_WIDTH_DIVISOR
    feet_h = h // FEET_HEIGHT_DIVISOR
    offset_x = (w - feet_w) // 2
    offset_y = h - feet_h
    feet = Collider(feet_w, feet_h, offset_x, offset_y)
    return MultiCollider({"body": body, "feet": feet})