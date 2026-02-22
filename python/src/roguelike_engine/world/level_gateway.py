from __future__ import annotations
from typing import Protocol


class ILevelGateway(Protocol):
    """
    Contrato mínimo que debe cumplir un gestor de nivel para integrarse con WorldManager.
    Adaptadores del juego (p.ej., MapManager) deben implementar este protocolo.
    """
    def serialize_state(self) -> dict: ...
    def deserialize_state(self, state: dict) -> None: ...
    def spawn_player(self, pos) -> None: ...
    def restore_npc_states(self, memory: dict) -> None: ...


class LevelGatewayFactory(Protocol):
    def create(self, level_name: str) -> ILevelGateway: ...


class DefaultLevelGatewayFactory:
    """
    Fábrica por defecto que intenta usar el MapManager del paquete del juego.
    Mantiene compatibilidad sin obligar a world a importar el juego si no está disponible.
    """
    def __init__(self):
        # Import lazy para evitar acoplamiento duro en import-time
        try:
            from roguelike_game.managers.map import MapManager  # type: ignore
            self._cls = MapManager
        except Exception as e:
            self._cls = None
            # No almacenamos el error para evitar warning de variable no usada

    def create(self, level_name: str) -> ILevelGateway:
        if not self._cls:
            raise RuntimeError(
                "DefaultLevelGatewayFactory no encontró MapManager del juego. "
                "Provee una LevelGatewayFactory propia al construir WorldManager."
            )
        return self._cls(level_name)  # type: ignore
