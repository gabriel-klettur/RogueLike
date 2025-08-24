"""
Package de gestión de ECS refactorizado.
"""
import logging

from .loader import ECSLoader
from .spawner import ECSSpawner
from .runner import ECSRunner

logger = logging.getLogger(__name__)
logger.setLevel(logging.INFO)

class ECSManager:
    """
    Orquesta ECS: carga mundo, spawn entidades, update y render.
    """
    def __init__(self, screen, map_manager, entities_manager, perf_log):
        self.perf_log = perf_log
        self.screen = screen
        self.map_manager = map_manager
        self.entities_manager = entities_manager

        self.loader = ECSLoader()
        self.spawner = ECSSpawner()
        self.runner = ECSRunner()

        # Crea ECSWorld
        self.ecs_world = self.loader.load(screen, map_manager, entities_manager.buildings, perf_log)

        # Spawn jugador (NPCs se spawnean vía sistema de spawners JSON)
        self.spawner.spawn_player(self.ecs_world, map_manager)

        # Enlaza entidades
        self.entities_manager.ecs_manager = self
        self.ecs_world.entities_manager = entities_manager

    def _get_initial_player_tile(self):
        return self.spawner._get_initial_player_tile(self.ecs_world, self.map_manager)

    def update(self, clock, screen, camera):
        self.runner.update(self.ecs_world, camera)

    def render(self, screen, camera):
        self.runner.render(self.ecs_world, screen, camera)
