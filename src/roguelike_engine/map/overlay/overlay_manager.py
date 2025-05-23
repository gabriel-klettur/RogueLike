# Path: src/roguelike_engine/map/overlay/overlay_manager.py

from typing import Optional, List
from .factory import get_overlay_store

# Instanciamos por defecto el store JSON
_default_store = get_overlay_store("json")

def load_overlay(map_name: str) -> Optional[List[List[str]]]:
    """
    Carga la capa overlay para un mapa dado usando la estrategia configurada.
    """
    return _default_store.load(map_name)

def save_overlay(map_name: str, overlay: List[List[str]]) -> None:
    """
    Guarda la capa overlay para un mapa dado usando la estrategia configurada.
    """
    _default_store.save(map_name, overlay)
