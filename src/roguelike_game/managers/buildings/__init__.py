"""
Package de gestión de edificios refactorizado.
"""
import logging

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
        self.updater.update(self.buildings, state, game_map, perf_log)
