# Path: src/roguelike_engine/map/core/manager.py

from typing import Optional

from src.roguelike_engine.config_map import (
    DUNGEON_WIDTH,
    DUNGEON_HEIGHT,
    LOBBY_OFFSET_X,
    LOBBY_OFFSET_Y,
)
from .service import MapService
from .model import MapData

# Instancia singleton de nuestro servicio
_default_service = MapService()

def build_map(
    *,
    width: int = DUNGEON_WIDTH,
    height: int = DUNGEON_HEIGHT,
    offset_x: int = LOBBY_OFFSET_X,
    offset_y: int = LOBBY_OFFSET_Y,
    map_mode: str = "combined",
    map_name: Optional[str] = None,
    export_debug: bool = True,
) -> MapData:
    """
    API de conveniencia para construir mapas sin instanciar MapService manualmente.
    Devuelve un MapData con matrix, tiles, overlay, metadata y name.
    """
    return _default_service.build_map(
        width=width,
        height=height,
        offset_x=offset_x,
        offset_y=offset_y,
        map_mode=map_mode,
        map_name=map_name,
        export_debug=export_debug,
    )
