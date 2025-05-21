# src/roguelike_engine/map/model/overlay/json_store.py

import os
import json
from typing import Optional, List
from pathlib import Path

from .interfaces import OverlayStore
from roguelike_engine.config.config import DATA_DIR

class JsonOverlayStore(OverlayStore):
    """
    Implementación de OverlayStore que persiste en JSON files,
    con soporte para overlays globales y por-zona.
    """
    def __init__(self, directory: str = None):
        # Directorio global de overlays (por defecto: DATA_DIR/map_overlays)
        self.global_dir = Path(directory) if directory else Path(DATA_DIR) / "map_overlays"
        # Directorio de overlays individuales por zona
        self.zones_dir  = Path(DATA_DIR) / "zones" / "overlays"

        os.makedirs(self.global_dir, exist_ok=True)
        os.makedirs(self.zones_dir,  exist_ok=True)

    def load(self, map_name: str) -> Optional[List[List[str]]]:
        """
        Carga la capa overlay para `map_name`, buscando sólo en zones/overlays.
        """
        # Intentar zona individual
        zone_path = self.zones_dir / f"{map_name}.overlay.json"
        if zone_path.is_file():
            with open(zone_path, "r", encoding="utf-8") as f:
                return json.load(f)
        # Devolvemos None si no hay overlay de zona
        return None

    def save(self, map_name: str, overlay: List[List[str]]) -> None:
        """
        Guarda el overlay en el directorio de zonas.
        """
        out_path = self.zones_dir / f"{map_name}.overlay.json"
        with open(out_path, "w", encoding="utf-8") as f:
            json.dump(overlay, f, ensure_ascii=False, indent=2)
