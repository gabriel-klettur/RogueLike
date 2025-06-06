# Path: src/roguelike_game/game/ecs_manager.py

from roguelike_game.ecs.core.manager import ECSWorld
from roguelike_game.ecs.factories.player_factory import spawn_player_tile
from roguelike_engine.config.map_config import global_map_settings


class ECSManager:
    def __init__(self, screen, map_manager, entities_manager, perf_log):
        self.perf_log = perf_log
        self.screen = screen
        self.map_manager = map_manager
        # Guardar gestor de entidades para colisiones con edificios
        self.entities_manager = entities_manager
        # Inicializar mundo ECS y pasar edificios para colisiones
        self.ecs_world = ECSWorld(screen, map_manager, entities_manager.buildings, perf_log)
        # Spawn de la entidad jugador según posición guardada en tile coords o centro del lobby
        saved_tile = self.map_manager._local_state.get("player_pos")
        # Validar que saved_tile sea secuencia de dos valores
        if isinstance(saved_tile, (tuple, list)) and len(saved_tile) == 2:
            tx, ty = saved_tile
        else:
            off_x, off_y = self.map_manager.lobby_offset
            tx = off_x + global_map_settings.zone_width // 2
            ty = off_y + global_map_settings.zone_height // 2
        # Crear entidad jugador en ECS usando spawn_player_tile para alinear collider 'feet'
        pid = spawn_player_tile(self.ecs_world, tx, ty)
        self.ecs_world.player_entity = pid
        # Registrar tile coords en MapManager para persistencia
        self.map_manager.spawn_player((tx, ty))
        self.entities_manager.ecs_manager = self

    def update(self, clock, screen, camera):
        # Actualiza la lógica del mundo ECS
        self.ecs_world.update(camera)

    def render(self, screen, camera):
        # Renderiza todas las entidades ECS en pantalla con cámara
        self.ecs_world.render(screen, camera)