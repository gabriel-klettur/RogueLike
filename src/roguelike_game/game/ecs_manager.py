from roguelike_game.ecs.world import NPCWorld
from roguelike_game.game.map_manager import MapManager

class ECSManager:
    def __init__(self, screen, map_manager):
        self.screen = screen
        self.map_manager = map_manager
        self.npc_world = NPCWorld(screen, map_manager)

    def update(self, clock, screen):
        # Actualiza la lógica del mundo ECS
        self.npc_world.update()

    def render(self, screen, camera):
        # Renderiza todas las entidades ECS en pantalla con cámara
        self.npc_world.render(screen, camera)