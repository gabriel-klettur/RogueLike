
# Path: src/roguelike_engine/map/overlay/json_store.py
import os
import json
from typing import Optional, List
from .interfaces import OverlayStore
from roguelike_engine.config_tiles import TILES_DATA_PATH

class JsonOverlayStore(OverlayStore):
    """
    Implementación de OverlayStore que persiste en JSON files.
    """

    def __init__(self, directory: str = None):
        # Carpeta donde se guardan los overlays
        self.directory = directory or TILES_DATA_PATH
        os.makedirs(self.directory, exist_ok=True)

    def load(self, map_name: str) -> Optional[List[List[str]]]:
        path = os.path.join(self.directory, f"{map_name}.overlay.json")
        if not os.path.isfile(path):
            return None
        with open(path, "r", encoding="utf-8") as f:
            return json.load(f)

    def save(self, map_name: str, overlay: List[List[str]]) -> None:
        path = os.path.join(self.directory, f"{map_name}.overlay.json")
        with open(path, "w", encoding="utf-8") as f:
            json.dump(overlay, f, ensure_ascii=False, indent=2)