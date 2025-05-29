import pygame

class Collider:
    """
    Componente que define el volumen de colisión.
    width, height: dimensiones del collider.
    offset_x, offset_y: desplazamiento relativo a Position.
    rect: pygame.Rect que representa la caja de colisión actual.
    """
    def __init__(self, width: int, height: int, offset_x: int = 0, offset_y: int = 0):
        self.width = width
        self.height = height
        self.offset_x = offset_x
        self.offset_y = offset_y
        self.rect = pygame.Rect(0, 0, width, height)
