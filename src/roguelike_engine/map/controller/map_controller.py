
# Path: src/roguelike_engine/map/controller/map_controller.py
from typing import Optional

from roguelike_engine.config_map import (
    ZONE_WIDTH,
    ZONE_HEIGHT
)
from roguelike_engine.map.controller.map_service import MapService
from roguelike_engine.map.model.map_model import Map

# Instancia singleton de nuestro servicio
_default_service = MapService()

def build_map(
    *,
    width: int = ZONE_WIDTH,
    height: int = ZONE_HEIGHT,
    offset_x: int = 0,
    offset_y: int = 0,
    map_mode: str = "combined",
    map_name: Optional[str] = None,
    export_debug: bool = True,
) -> Map:
    """
    API de conveniencia para construir mapas sin instanciar MapService manualmente.
    Devuelve un Map con matrix, tiles, overlay, metadata y name.
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