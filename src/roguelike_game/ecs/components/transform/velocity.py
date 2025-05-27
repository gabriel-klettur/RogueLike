class Velocity:
    """
    Componente que almacena la intención de movimiento.
    vx, vy: desplazamientos en píxeles por frame.
    """
    def __init__(self, vx: float = 0, vy: float = 0):
        self.vx = vx
        self.vy = vy
