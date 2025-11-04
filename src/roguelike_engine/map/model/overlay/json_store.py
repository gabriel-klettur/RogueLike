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
        # Resolución del directorio se hace dinámicamente por mundo activo en load/save
        # para evitar capturar rutas del mundo 'base' antes de un teleport.
        pass

    def load(self, map_name: str) -> Optional[List[List[str]]]:
        """
        Carga la capa overlay para `map_name`, usando configuración de zonas.
        """
        # Directorio dinámico por mundo activo
        zones_dir = global_map_settings.overlays_dir
        os.makedirs(zones_dir, exist_ok=True)
        # Determinar zona según configuración (normalizar sentinelas)
        zn = str(map_name)
        zl = zn.replace('_', ' ').lower()
        if zl in ("no zone", "no-zone"):
            zone_name = "no zone"
        elif zn in global_map_settings.zone_offsets.keys():
            zone_name = zn
        else:
            zone_name = "no zone"
        zone_path = zones_dir / f"{zone_name}.overlay.json"
        if zone_path.is_file():
            with open(zone_path, "r", encoding="utf-8") as f:
                data = json.load(f)
                try:
                    world = getattr(global_map_settings, 'current_world', '?')
                    if isinstance(data, dict) and 'layers' in data:
                        layers = data.get('layers', {})
                        counts = {k: sum(1 for row in v for x in row if x) for k, v in layers.items() if isinstance(v, list)}
                        logger.info(f"[JsonOverlayStore] load world={world} zone='{zone_name}' file='{zone_path}' counts={counts}")
                    else:
                        logger.info(f"[JsonOverlayStore] load world={world} zone='{zone_name}' file='{zone_path}' type={type(data)} rows={len(data) if isinstance(data, list) else 'n/a'}")
                except Exception:
                    pass
                return data
        return None

    def save(self, map_name: str, overlay: List[List[str]]) -> None:
        """
        Guarda el overlay usando configuración de zonas.
        """
        # Directorio dinámico por mundo activo
        zones_dir = global_map_settings.overlays_dir
        os.makedirs(zones_dir, exist_ok=True)
        # Determinar zona según configuración (normalizar sentinelas)
        zn = str(map_name)
        zl = zn.replace('_', ' ').lower()
        if zl in ("no zone", "no-zone"):
            zone_name = "no zone"
        elif zn in global_map_settings.zone_offsets.keys():
            zone_name = zn
        else:
            zone_name = "no zone"
        out_path = zones_dir / f"{zone_name}.overlay.json"
        with open(out_path, "w", encoding="utf-8") as f:
            json.dump(overlay, f, ensure_ascii=False, indent=2)
            try:
                world = getattr(global_map_settings, 'current_world', '?')
                if isinstance(overlay, dict) and 'layers' in overlay:
                    layers = overlay.get('layers', {})
                    counts = {k: sum(1 for row in v for x in row if x) for k, v in layers.items() if isinstance(v, list)}
                    logger.info(f"[JsonOverlayStore] save world={world} zone='{zone_name}' file='{out_path}' counts={counts}")
                else:
                    logger.info(f"[JsonOverlayStore] save world={world} zone='{zone_name}' file='{out_path}' type={type(overlay)} rows={len(overlay) if isinstance(overlay, list) else 'n/a'}")
            except Exception:
                pass