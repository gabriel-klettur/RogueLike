
# Path: src/roguelike_engine/map/model/loader/factory.py
from typing import Dict
from .interfaces import MapLoader
from .text_loader_strategy import TextMapLoader
# from .json_loader_strategy import JsonMapLoader    # futuro

_LOADERS: Dict[str, type[MapLoader]] = {
    "text": TextMapLoader,
    # "json": JsonMapLoader,
}

def get_map_loader(name: str = "text") -> MapLoader:
    cls = _LOADERS.get(name)
    if not cls:
        raise ValueError(f"MapLoader desconocido: {name!r}")
    return cls()