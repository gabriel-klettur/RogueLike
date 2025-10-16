from typing import Optional, List, Dict
from .factory import get_overlay_store
from roguelike_engine.map.model.layer import Layer

import logging
import sys
logger = logging.getLogger(__name__)

# Instanciamos por defecto el store JSON usando sólo overlays de zonas
_default_store = get_overlay_store("json")
# Optional store for tests/tools to force usage
_injected_store = None

def set_overlay_store(store) -> None:
    """Permite inyectar un store de overlays (para tests/herramientas)."""
    global _default_store, _injected_store
    _default_store = store
    _injected_store = store

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
    logger.debug(f" load_layers called for map '{map_name}'")
    raw = _default_store.load(map_name)
    #logger.debug(f" store.load raw for '{map_name}': {raw}")
    if raw is None:
        logger.debug(f" no overlay data for '{map_name}'")
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
    logger.debug(f" parsed layers for '{map_name}': {list(result.keys())}")
    return result

def save_layers(map_name: str, layers: Dict[Layer, List[List[str]]]) -> None:
    """
    Guarda múltiples capas en el formato nuevo JSON.
    """
    data = serialize_layers_payload(layers)
    # Acceder al atributo del módulo en tiempo de ejecución (respetando monkeypatch)
    mod = sys.modules[__name__]
    store = getattr(mod, "_injected_store", None) or mod._default_store
    store.save(map_name, data)

def serialize_layers_payload(layers: Dict[Layer, List[List[str]]]) -> Dict[str, List[List[str]]]:
    """Serializa el diccionario {Layer: grid} al formato persistible {"layers": {name: grid}}."""
    return {"layers": {layer.name: grid for layer, grid in layers.items()}}