# src/roguelike_engine/map/generator/factory.py
# Path: src/roguelike_engine/map/generator/factory.py
from typing import Dict
from .interfaces import MapGenerator
from .dungeon import DungeonGenerator
# from .cellular_automata import CellularAutomataGenerator
# from .prefab import PrefabGenerator

_GENERATORS: Dict[str, type[MapGenerator]] = {
    "dungeon": DungeonGenerator,
    # "cellular": CellularAutomataGenerator,
    # "prefab": PrefabGenerator,
}

def get_generator(name: str = "dungeon") -> MapGenerator:
    cls = _GENERATORS.get(name)
    if not cls:
        raise ValueError(f"Generador desconocido: {name}")
    return cls()