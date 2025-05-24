from roguelike_game.ecs.world import NPCWorld

class ECSManager:
    def __init__(self, screen):
        self.screen = screen
        self.npc_world = NPCWorld(screen)

    def update(self, clock, screen):
        # Actualiza la lógica del mundo ECS
        self.npc_world.update()

    def render(self, screen, camera):
        # Renderiza todas las entidades ECS en pantalla con cámara
        self.npc_world.render(screen, camera)