# Path: src/roguelike_game/ecs/components/combat/health.py
from dataclasses import dataclass

@dataclass
class Health:
    """
    Componente que define la salud (HP) de una entidad.
    current_hp: puntos de vida actuales.
    max_hp: puntos de vida máximos.
    """
    current_hp: int
    max_hp: int