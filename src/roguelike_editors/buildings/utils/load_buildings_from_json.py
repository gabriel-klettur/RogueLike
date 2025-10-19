import os
from typing import List
from roguelike_engine.config.config import (
    BUILDINGS_COLLISIONS_BY_IMAGE_PATH,
    BUILDINGS_COLLISIONS_BY_SPAWN_ID_PATH,
    BUILDINGS_COLLISIONS_BY_BUILDING_INSTANCE_ID_PATH,
    BUILDINGS_TEMPLATES_PATH,
    BUILDINGS_INSTANCES_PATH,
)
import logging

logger = logging.getLogger(__name__)

# Delegate heavy logic to helpers (smaller, testable modules)
import roguelike_engine.config.config as _cfg
from roguelike_editors.buildings.utils import (
    building_assembler as _assembler,
    collisions_io as _collio,
    split_io as _splitio,
)


def _sync_paths_to_helpers() -> None:
    """Propagate any test-patched paths from this module to config and helper modules.

    Tests patch constants on this module (not on roguelike_engine.config). We mirror them
    into the config module and helper modules that captured names at import time.
    """
    names = [
        "BUILDINGS_TEMPLATES_PATH",
        "BUILDINGS_INSTANCES_PATH",
        "BUILDINGS_COLLISIONS_BY_IMAGE_PATH",
        "BUILDINGS_COLLISIONS_BY_SPAWN_ID_PATH",
        "BUILDINGS_COLLISIONS_BY_BUILDING_INSTANCE_ID_PATH",
    ]
    for n in names:
        try:
            v = globals().get(n)
            if v:
                setattr(_cfg, n, v)
                # Also mirror onto helper modules that imported constants by name
                try:
                    setattr(_splitio, n, v)
                except Exception:
                    pass
                try:
                    setattr(_collio, n, v)
                except Exception:
                    pass
        except Exception:
            continue


def load_buildings_from_json(z_state=None) -> List:
    """Carga edificios desde JSON en modo split (templates + instances).

    - Mantiene la API pública existente y compatibilidad con tests que hacen monkeypatch
      de rutas en este módulo.
    - No hay fallback legacy: si faltan archivos split, retorna [].
    """
    # Use possibly patched paths from this module
    t_path = globals().get("BUILDINGS_TEMPLATES_PATH", BUILDINGS_TEMPLATES_PATH)
    i_path = globals().get("BUILDINGS_INSTANCES_PATH", BUILDINGS_INSTANCES_PATH)

    if os.path.exists(t_path) and os.path.exists(i_path):
        _sync_paths_to_helpers()
        return _assembler.load_from_split(z_state)

    logger.warning(
        "[Buildings][split] Archivos requeridos no encontrados: templates=%s instances=%s. No se cargan edificios.",
        t_path,
        i_path,
    )
    return []