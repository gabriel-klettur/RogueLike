from typing import Optional, List, Dict
from .factory import get_overlay_store
from roguelike_engine.map.model.layer import Layer

import logging
logger = logging.getLogger(__name__)

# Instanciamos por defecto el store JSON usando sólo overlays de zonas
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

def load_layers(map_name: str) -> Dict[Layer, List[List[str]]]:
    """
    Carga todas las capas de overlay para un mapa dado.
    Devuelve diccionario Layer -> matriz de códigos.
    """
    logger.debug(f"[DEBUG][OverlayManager] load_layers called for map '{map_name}'")
    raw = _default_store.load(map_name)
    #logger.debug(f"[DEBUG][OverlayManager] store.load raw for '{map_name}': {raw}")
    if raw is None:
        logger.debug(f"[DEBUG][OverlayManager] no overlay data for '{map_name}'")
        return {}
    # Si formato antiguo (lista), asignar a Ground
    if isinstance(raw, list):
        return {Layer.Ground: raw}
    # Si formato nuevo {'layers': {...}}
    layers_dict = raw.get("layers", {}) if isinstance(raw, dict) else {}
    result: Dict[Layer, List[List[str]]] = {}
    for name, grid in layers_dict.items():
        try:
            layer = Layer[name]
        except KeyError:
            continue
        result[layer] = grid
    logger.debug(f"[DEBUG][OverlayManager] parsed layers for '{map_name}': {list(result.keys())}")
    return result

def save_layers(map_name: str, layers: Dict[Layer, List[List[str]]]) -> None:
    """
    Guarda múltiples capas en el formato nuevo JSON.
    """
    data = {"layers": {layer.name: grid for layer, grid in layers.items()}}
    _default_store.save(map_name, data)