from roguelike_game.ecs.world import NPCWorld
from roguelike_game.game.map_manager import MapManager
from roguelike_game.ecs.factories.player_factory import spawn_player
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.config.map_config import global_map_settings
from roguelike_game.config_player import RENDERED_SPRITE_SIZE

class ECSManager:
    def __init__(self, screen, map_manager, entities_manager):
        self.screen = screen
        self.map_manager = map_manager
        # Guardar gestor de entidades para colisiones con edificios
        self.entities_manager = entities_manager
        # Inicializar mundo ECS y pasar edificios para colisiones
        self.npc_world = NPCWorld(screen, map_manager, entities_manager.buildings)
        # Spawn de la entidad jugador en ECS en el centro del lobby
        tx, ty = map_manager.lobby_offset
        # Calcular centro de la sala de lobby (en tiles)
        ct_x = tx + global_map_settings.zone_width // 2
        ct_y = ty + global_map_settings.zone_height // 2
        # Convertir a píxeles y centrar sprite del jugador
        px = ct_x * TILE_SIZE - RENDERED_SPRITE_SIZE[0] // 2
        py = ct_y * TILE_SIZE - RENDERED_SPRITE_SIZE[1] // 2
        pid = spawn_player(self.npc_world, px, py)
        self.npc_world.player_entity = pid
        self.entities_manager.ecs_manager = self

    def update(self, clock, screen, camera):
        # Actualiza la lógica del mundo ECS
        self.npc_world.update(camera)

    def render(self, screen, camera):
        # Renderiza todas las entidades ECS en pantalla con cámara
        self.npc_world.render(screen, camera)