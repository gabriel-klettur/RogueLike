import os
import json
from typing import Optional, List
from pathlib import Path

from .interfaces import OverlayStore
from roguelike_engine.config import map_config
from roguelike_engine.config import config as engine_config

import logging
logger = logging.getLogger(__name__)

# Expose DATA_DIR for tests to monkeypatch; default to engine config
DATA_DIR = getattr(engine_config, "DATA_DIR", ".")

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
        # Directorio dinámico por mundo activo, con fallback a DATA_DIR
        zones_dir = getattr(map_config.global_map_settings, 'overlays_dir', None)
        if not zones_dir:
            zones_dir = Path(DATA_DIR) / 'map' / 'zones' / 'overlays'
        zones_dir = Path(zones_dir)
        os.makedirs(zones_dir, exist_ok=True)

        # Determinar zona según configuración (normalizar sentinelas)
        zn = str(map_name)
        zl = zn.replace('_', ' ').lower()
        if zl in ("no zone", "no-zone", "no_zone"):
            zone_name = "no_zone"
        elif zn in map_config.global_map_settings.zone_offsets.keys():
            zone_name = zn
        else:
            zone_name = "no_zone"
        zone_path = zones_dir / f"{zone_name}.overlay.json"

        if zone_path.is_file():
            with open(zone_path, "r", encoding="utf-8") as f:
                data = json.load(f)
                try:
                    world = getattr(map_config.global_map_settings, 'current_world', '?')
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
        # Directorio dinámico por mundo activo, con fallback a DATA_DIR
        zones_dir = getattr(map_config.global_map_settings, 'overlays_dir', None)
        if not zones_dir:
            zones_dir = Path(DATA_DIR) / 'map' / 'zones' / 'overlays'
        zones_dir = Path(zones_dir)
        os.makedirs(zones_dir, exist_ok=True)
        # Determinar zona según configuración (normalizar sentinelas)
        zn = str(map_name)
        zl = zn.replace('_', ' ').lower()
        if zl in ("no zone", "no-zone", "no_zone"):
            zone_name = "no_zone"
        elif zn in map_config.global_map_settings.zone_offsets.keys():
            zone_name = zn
        else:
            zone_name = "no_zone"
        out_path = zones_dir / f"{zone_name}.overlay.json"
        # Ensure parent directories exist (belt and suspenders)
        out_path.parent.mkdir(parents=True, exist_ok=True)
        with open(out_path, "w", encoding="utf-8") as f:
            json.dump(overlay, f, ensure_ascii=False, indent=2)
            try:
                world = getattr(map_config.global_map_settings, 'current_world', '?')
                if isinstance(overlay, dict) and 'layers' in overlay:
                    layers = overlay.get('layers', {})
                    counts = {k: sum(1 for row in v for x in row if x) for k, v in layers.items() if isinstance(v, list)}
                    logger.info(f"[JsonOverlayStore] save world={world} zone='{zone_name}' file='{out_path}' counts={counts}")
                else:
                    logger.info(f"[JsonOverlayStore] save world={world} zone='{zone_name}' file='{out_path}' type={type(overlay)} rows={len(overlay) if isinstance(overlay, list) else 'n/a'}")
            except Exception:
                pass