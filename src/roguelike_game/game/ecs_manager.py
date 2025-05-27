from roguelike_game.ecs.world import NPCWorld
from roguelike_game.game.map_manager import MapManager
from roguelike_game.ecs.factories.player_factory import spawn_player

class ECSManager:
    def __init__(self, screen, map_manager, entities_manager):
        self.screen = screen
        self.map_manager = map_manager
        # Guardar gestor de entidades para colisiones con edificios
        self.entities_manager = entities_manager
        # Inicializar mundo ECS y pasar edificios para colisiones
        self.npc_world = NPCWorld(screen, map_manager, entities_manager.buildings)
        self.npc_world.player = entities_manager.player
        # Spawn de la entidad jugador en ECS
        pid = spawn_player(self.npc_world, entities_manager.player.x, entities_manager.player.y, entities_manager.player.model.character_name)
        self.npc_world.player_entity = pid

    def update(self, clock, screen):
        # Actualiza la lógica del mundo ECS
        self.npc_world.update()

    def render(self, screen, camera):
        # Renderiza todas las entidades ECS en pantalla con cámara
        self.npc_world.render(screen, camera)