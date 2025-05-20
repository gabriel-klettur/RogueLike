# src/roguelike_game/game/player_data.py

from typing import Dict, Any

class PlayerData:
    """
    Guarda datos globales del jugador que persisten entre niveles:
    - last_position: dict[nivel, (tile_x, tile_y)]
    - inventario, stats, etc. (añadir según necesidades)
    """
    def __init__(self):
        self.last_position: Dict[str, tuple[int, int]] = {}
        # Aquí puedes añadir inventario, logros, etc.

    def to_dict(self) -> Dict[str, Any]:
        return {
            "last_position": self.last_position,
            # serializa más campos si los añades...
        }

    @classmethod
    def from_dict(cls, data: Dict[str, Any]) -> "PlayerData":
        pd = cls()
        pd.last_position = data.get("last_position", {})
        # deserializa más campos si los añades...
        return pd
