import pygame

typing = __import__('typing')

class MaskCollider:
    """
    Componente para colisión basada en máscara de píxeles.
    mask: pygame.Mask
    offset_x, offset_y: desplazamiento relativo a Position.
    """
    def __init__(self, mask: pygame.Mask, offset_x: int = 0, offset_y: int = 0):
        self.mask = mask
        self.offset_x = offset_x
        self.offset_y = offset_y
