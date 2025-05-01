#Path: src/roguelike_engine/map/merger/factory.py

from typing import Dict
from .interfaces import MergerStrategy
from .center_to_center import CenterToCenterMerger
from .edge_to_edge import EdgeToEdgeMerger

_MERGERS: Dict[str, type[MergerStrategy]] = {
    "center_to_center": CenterToCenterMerger,
    "edge_to_edge": EdgeToEdgeMerger,
}


def get_merger(name: str = "center_to_center") -> MergerStrategy:
    cls = _MERGERS.get(name)
    if not cls:
        raise ValueError(f"Merger desconocido: {name}")
    return cls()
