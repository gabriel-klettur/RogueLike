# Path: src/roguelike_game/ecs/components/ai/damage_config.py
from dataclasses import dataclass

@dataclass
class DamageConfig:
    """
    Configuración de duración de daño para NPCs.
    """
    duration: float