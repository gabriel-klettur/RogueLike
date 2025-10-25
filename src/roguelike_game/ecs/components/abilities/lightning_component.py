from roguelike_game.ecs.components.abilities.lightning_model import LightningModel

class LightningComponent:
    """
    Componente ECS para efecto de rayo Lightning.
    """
    def __init__(self, start_pos: tuple[float, float], end_pos: tuple[float, float],
                 segments: int, offset: int, lifetime: int,
                 *, preset_id: str | None = None,
                 colors_palette: list[tuple[int, int, int]] | None = None,
                 particle_size: int = 2,
                 particle_lifespan: int = 1,
                 particle_emit_rate: int = 2,
                 particle_speed: float = 0.0,
                 particle_dispersion: float = 0.0,
                 size_min: int | None = None,
                 size_max: int | None = None,
                 # Advanced optional particle params
                 particle_blend_mode: str | None = None,
                 particle_size_over_life: list | tuple | None = None,
                 particle_alpha_over_life: list | tuple | None = None,
                 particle_color_over_life: list | tuple | None = None,
                 particle_gravity: tuple[float, float] | list[float] | float | None = None,
                 particle_drag: float | None = None,
                 ):
        # Modelo de los puntos del rayo
        self.model = LightningModel(start_pos, end_pos, segments=segments, offset=offset, lifetime=lifetime)
        # Parámetros opcionales para emisión de partículas
        self.preset_id = preset_id
        self.colors_palette = list(colors_palette) if colors_palette else None
        try:
            self.particle_size = int(particle_size)
        except Exception:
            self.particle_size = 2
        try:
            self.particle_lifespan = int(particle_lifespan)
        except Exception:
            self.particle_lifespan = 1
        # Emisión desde preset
        try:
            self.particle_emit_rate = int(particle_emit_rate)
        except Exception:
            self.particle_emit_rate = 2
        try:
            self.particle_speed = float(particle_speed)
        except Exception:
            self.particle_speed = 0.0
        try:
            # Interpretado como desviación angular (radianes) para la dirección de la partícula
            self.particle_dispersion = float(particle_dispersion)
        except Exception:
            self.particle_dispersion = 0.0
        # Rango de tamaños opcional
        try:
            self.size_min = int(size_min) if size_min is not None else None
            self.size_max = int(size_max) if size_max is not None else None
        except Exception:
            self.size_min = None
            self.size_max = None
        # Advanced particle fields (optional)
        self.particle_blend_mode = particle_blend_mode if isinstance(particle_blend_mode, str) else None
        self.particle_size_over_life = particle_size_over_life if isinstance(particle_size_over_life, (list, tuple)) else None
        self.particle_alpha_over_life = particle_alpha_over_life if isinstance(particle_alpha_over_life, (list, tuple)) else None
        self.particle_color_over_life = particle_color_over_life if isinstance(particle_color_over_life, (list, tuple)) else None
        self.particle_gravity = particle_gravity
        self.particle_drag = float(particle_drag) if isinstance(particle_drag, (int, float)) else None

    def update(self):
        self.model.update()

    def is_finished(self) -> bool:
        return self.model.is_finished()