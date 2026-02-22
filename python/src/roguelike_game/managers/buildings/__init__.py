"""
Package de gestión de edificios refactorizado.
"""
import logging
from roguelike_engine.config.map_config import global_map_settings

from ..map import MapManager
from .loader import BuildingsLoader
from .calibrator import BuildingsCalibrator
from .updater import BuildingsUpdater

logger = logging.getLogger(__name__)
logger.setLevel(logging.INFO)

class BuildingsManager:
    """
    Carga y mantiene edificios del juego.
    """
    def __init__(self, z_state, game_map: MapManager):
        self.z_state = z_state
        self.map = game_map

        self.loader = BuildingsLoader()
        self.calibrator = BuildingsCalibrator()
        self.updater = BuildingsUpdater()

        self.buildings = []
        self._world_loaded: str | None = getattr(global_map_settings, 'current_world', 'base')
        self.init_buildings()

    def init_buildings(self):
        """
        Carga edificios. Devuelve buildings.
        """
        self.buildings = self.loader.load(self.z_state)
        self.calibrator.recalibrate(self.buildings)
        return self.buildings

    def update(self, state, game_map, perf_log):
        """
        Actualiza edificios.
        """
        # If world changed, reload buildings from the new world's instances
        try:
            cur_world = getattr(global_map_settings, 'current_world', 'base')
            if self._world_loaded != cur_world:
                self.init_buildings()
                self._world_loaded = cur_world
        except Exception:
            pass
        self.updater.update(self.buildings, state, game_map, perf_log)
