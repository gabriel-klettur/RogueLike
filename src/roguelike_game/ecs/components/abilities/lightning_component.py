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
                 particle_lifespan: int = 1):
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

    def update(self):
        self.model.update()

    def is_finished(self) -> bool:
        return self.model.is_finished()