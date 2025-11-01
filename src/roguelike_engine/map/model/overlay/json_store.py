import os
import json
from typing import Optional, List
from pathlib import Path

from .interfaces import OverlayStore
from roguelike_engine.config.map_config import global_map_settings

import logging
logger = logging.getLogger(__name__)

class JsonOverlayStore(OverlayStore):
    """
    Implementación de OverlayStore que persiste en JSON files,
    con soporte para overlays globales y por-zona.
    """
    def __init__(self, directory: str = None):
        # Directorio de overlays individuales por zona (dependiente del mundo activo)
        self.zones_dir  = global_map_settings.overlays_dir
        os.makedirs(self.zones_dir,  exist_ok=True)

    def load(self, map_name: str) -> Optional[List[List[str]]]:
        """
        Carga la capa overlay para `map_name`, usando configuración de zonas.
        """
        # Determinar zona según configuración
        zone_name = map_name if map_name in global_map_settings.zone_offsets.keys() else "no_zone"
        zone_path = self.zones_dir / f"{zone_name}.overlay.json"
        if zone_path.is_file():
            with open(zone_path, "r", encoding="utf-8") as f:
                data = json.load(f)
                logger.debug(f" load zone '{zone_name}', data_type={type(data)}, rows={len(data) if isinstance(data, list) else 'n/a'}")
                return data
        return None

    def save(self, map_name: str, overlay: List[List[str]]) -> None:
        """
        Guarda el overlay usando configuración de zonas.
        """
        # Determinar zona según configuración
        zone_name = map_name if map_name in global_map_settings.zone_offsets.keys() else "no_zone"
        out_path = self.zones_dir / f"{zone_name}.overlay.json"
        with open(out_path, "w", encoding="utf-8") as f:
            json.dump(overlay, f, ensure_ascii=False, indent=2)
            logger.debug(f" saved zone '{zone_name}', data_type={type(overlay)}, rows={len(overlay) if isinstance(overlay, list) else 'n/a'}")