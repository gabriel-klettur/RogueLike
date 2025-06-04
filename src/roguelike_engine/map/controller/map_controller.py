
# Path: src/roguelike_engine/map/controller/map_controller.py
from typing import Optional

from roguelike_engine.map.controller.map_service import MapService
from roguelike_engine.map.model.map_model import Map

# Instancia "singleton" de nuestro servicio
_default_service = MapService()

def build_map(map_name: Optional[str] = None) -> Map:
    """
    API de conveniencia para construir mapas sin instanciar MapService manualmente.
    Devuelve un Map con matrix, tiles, overlay, metadata y name.
    """
    return _default_service.build_map(map_name)