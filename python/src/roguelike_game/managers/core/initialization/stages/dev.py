from __future__ import annotations

import logging

import roguelike_engine.config.config as config
from roguelike_engine.buildings import auto_importer as _auto_importer

from ..types import InitContext

logger = logging.getLogger(__name__)


def dev_auto_import_buildings(ctx: InitContext) -> None:
    """Escanea assets/buildings y crea nuevas plantillas/instancias si la flag DEV está activa."""
    try:
        if bool(getattr(config, "DEV_AUTO_IMPORT_BUILDINGS", False)):
            try:
                _auto_importer.run(verbose=True)
            except Exception as e:
                logger.warning(f"[AutoImporter] Error al auto-importar: {e}")
    except Exception as e:
        logger.debug(f"[Initializer] Config no disponible para auto-import: {e}")
