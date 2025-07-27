from roguelike_game.ecs.components.abilities.lightning_model import LightningModel

class LightningComponent:
    """
    Componente ECS para efecto de rayo Lightning.
    """
    def __init__(self, start_pos: tuple[float, float], end_pos: tuple[float, float],
                 segments: int, offset: int, lifetime: int):
        # Modelo de los puntos del rayo
        self.model = LightningModel(start_pos, end_pos, segments=segments, offset=offset, lifetime=lifetime)

    def update(self):
        self.model.update()

    def is_finished(self) -> bool:
        return self.model.is_finished()