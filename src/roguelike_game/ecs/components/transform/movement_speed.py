# Path: src/roguelike_game/ecs/components/transform/movement_speed.py
from dataclasses import dataclass

@dataclass
class MovementSpeed:
    """
    Componente que define la velocidad de movimiento (píxeles por actualización).
    """
    speed: float = 1.0