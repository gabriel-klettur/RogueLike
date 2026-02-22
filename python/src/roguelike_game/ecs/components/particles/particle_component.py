class ParticleComponent:
    """
    ECS component para una partícula.
    dx, dy: velocidad por tick.
    color: tupla RGB.
    size: tamaño en píxeles.
    lifespan: duración en ticks.
    age: edad actual en ticks.

    Campos avanzados opcionales (retrocompatibles):
    - gravity: (gx, gy) o número (gy)
    - drag: amortiguación 0..1 (recomendado <= 0.1)
    - blend_mode: "additive" o "alpha"
    - size_over_life / alpha_over_life / color_over_life: curvas en t∈[0,1]
    """
    def __init__(
        self,
        dx: float,
        dy: float,
        color: tuple,
        size: int,
        lifespan: int,
        anchor_eid=None,
        gravity=None,
        drag=None,
        blend_mode: str | None = None,
        size_over_life=None,
        alpha_over_life=None,
        color_over_life=None,
        texture_path: str | None = None,
        flipbook: dict | None = None,
    ):
        self.dx = float(dx)
        self.dy = float(dy)
        self.color = tuple(color)
        self.size = int(size)
        self.lifespan = int(lifespan)
        self.age = 0
        # Seguimiento opcional a una entidad (por ejemplo, el jugador durante un slash)
        self.anchor_eid = anchor_eid
        # Última posición del ancla para calcular delta por frame (se inicializa en el sistema)
        self.anchor_last_x = None
        self.anchor_last_y = None
        # Avanzados
        # Gravedad: aceptar float (gy) o par (gx, gy)
        if isinstance(gravity, (int, float)):
            self.gx = 0.0
            self.gy = float(gravity)
        elif isinstance(gravity, (list, tuple)) and len(gravity) >= 2:
            try:
                self.gx = float(gravity[0])
                self.gy = float(gravity[1])
            except Exception:
                self.gx = 0.0
                self.gy = 0.0
        else:
            self.gx = 0.0
            self.gy = 0.0
        try:
            self.drag = float(drag) if isinstance(drag, (int, float)) else 0.0
        except Exception:
            self.drag = 0.0
        # Clamp seguro
        if self.drag < 0.0:
            self.drag = 0.0
        if self.drag > 0.98:
            self.drag = 0.98
        self.blend_mode = blend_mode if isinstance(blend_mode, str) else None
        # Curvas/gradientes tal cual; el renderer las valida/evalúa
        self.size_over_life = size_over_life if isinstance(size_over_life, (list, tuple)) else None
        self.alpha_over_life = alpha_over_life if isinstance(alpha_over_life, (list, tuple)) else None
        self.color_over_life = color_over_life if isinstance(color_over_life, (list, tuple)) else None
        # Valores base para curvas
        self.base_size = int(size)
        self.base_color = tuple(color)
        # Nota: zoom se aplica en el renderer
        # Texturizado opcional
        self.texture_path = texture_path if isinstance(texture_path, str) else None
        self.flipbook = dict(flipbook) if isinstance(flipbook, dict) else None
# Path: src/roguelike_game/ecs/components/particles/particle_component.py