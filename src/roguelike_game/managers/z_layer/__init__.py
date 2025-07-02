"""
Package de gestión de Z-layer refactorizado.
"""
import logging

from .assigner import ZLayerAssigner

logger = logging.getLogger(__name__)
logger.setLevel(logging.INFO)

class ZLayerManager:
    """
    Orquesta asignación de capas Z a entidades.
    """
    def __init__(self, z_state):
        self.z_state = z_state
        self.assigner = ZLayerAssigner()

    def initialize(self, state, entities):
        """
        Inicializa z_state del juego y asigna capas Z.
        """
        state.z_state = self.z_state
        self.assigner.assign(self.z_state, entities)
