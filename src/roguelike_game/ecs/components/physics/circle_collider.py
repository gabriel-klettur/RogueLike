import pygame

class CircleCollider:
    """
    Componente para colisiones circulares.
    radius: radio del círculo en píxeles.
    offset_x, offset_y: desplazamiento del CENTRO del círculo relativo a Position (en coordenadas del sprite).
    """
    __slots__ = ("radius", "offset_x", "offset_y")

    def __init__(self, radius: int, offset_x: int = 0, offset_y: int = 0):
        if radius <= 0:
            raise ValueError("CircleCollider.radius must be > 0")
        self.radius = int(radius)
        self.offset_x = int(offset_x)
        self.offset_y = int(offset_y)
