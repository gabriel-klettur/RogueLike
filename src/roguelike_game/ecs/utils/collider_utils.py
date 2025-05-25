# src/roguelike_game/ecs/utils/collider_utils.py

import pygame
from ..components.collider import Collider
from ..components.mask_collider import MaskCollider

def build_collider_rect(pos_x: float,
                        pos_y: float,
                        collider: Collider | MaskCollider) -> pygame.Rect:
    """
    Retorna el pygame.Rect ubicado en (pos_x,pos_y) con el offset y tamaño del collider.
    """
    # Soporta Collider y MaskCollider (usa mask.get_size())
    if isinstance(collider, MaskCollider):
        w, h = collider.mask.get_size()
    else:
        w = collider.width
        h = getattr(collider, "height", collider.width)
    return pygame.Rect(
        pos_x + collider.offset_x,
        pos_y + collider.offset_y,
        w,
        h
    )