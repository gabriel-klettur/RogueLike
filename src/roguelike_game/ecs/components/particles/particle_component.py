class ParticleComponent:
    """
    ECS component para una partícula.
    dx, dy: velocidad por tick.
    color: tupla RGB.
    size: tamaño en píxeles.
    lifespan: duración en ticks.
    age: edad actual en ticks.
    """
    def __init__(self, dx: float, dy: float, color: tuple, size: int, lifespan: int):
        self.dx = dx
        self.dy = dy
        self.color = color
        self.size = size
        self.lifespan = lifespan
        self.age = 0