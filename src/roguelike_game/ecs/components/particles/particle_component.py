class ParticleComponent:
    """
    ECS component para una partícula.
    dx, dy: velocidad por tick.
    color: tupla RGB.
    size: tamaño en píxeles.
    lifespan: duración en ticks.
    age: edad actual en ticks.
    """
    def __init__(self, dx: float, dy: float, color: tuple, size: int, lifespan: int, anchor_eid=None):
        self.dx = dx
        self.dy = dy
        self.color = color
        self.size = size
        self.lifespan = lifespan
        self.age = 0
        # Seguimiento opcional a una entidad (por ejemplo, el jugador durante un slash)
        self.anchor_eid = anchor_eid
        # Última posición del ancla para calcular delta por frame (se inicializa en el sistema)
        self.anchor_last_x = None
        self.anchor_last_y = None
# Path: src/roguelike_game/ecs/components/particles/particle_component.py