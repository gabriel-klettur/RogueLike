import random
from dataclasses import dataclass

@dataclass
class TileAvoidance:
    """
    Componente que almacena el movimiento suave de un NPC para evitar colisión de pies en el mismo tile.
    dx: dirección en X (-1,0,1)
    dy: dirección en Y (-1,0,1)
    speed: velocidad en píxeles por actualización
    origin_tile: tupla (tx,ty) del tile original
    """
    dx: int
    dy: int
    speed: float
    origin_tile: tuple[int, int]

    @staticmethod
    def random_direction():
        """Devuelve una dirección aleatoria cardinal (arriba/abajo/izq/der)."""
        dirs = [(-1, 0), (1, 0), (0, -1), (0, 1)]
        return random.choice(dirs)
