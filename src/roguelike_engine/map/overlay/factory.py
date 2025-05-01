# Path: src/roguelike_engine/map/overlay/factory.py

from typing import Dict
from .interfaces import OverlayStore
from .json_store import JsonOverlayStore

_STORE_CLASSES: Dict[str, type[OverlayStore]] = {
    "json": JsonOverlayStore,
    # en el futuro: "db": DatabaseOverlayStore, ...
}

def get_overlay_store(name: str = "json", **kwargs) -> OverlayStore:
    """
    Devuelve una instancia del OverlayStore registrado bajo `name`.
    """
    cls = _STORE_CLASSES.get(name)
    if cls is None:
        raise ValueError(f"OverlayStore desconocido: {name!r}")
    return cls(**kwargs)
