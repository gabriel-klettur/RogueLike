import pygame
from roguelike_game.ecs.components.fsm.npc_state import NPCState

class StatesDebugRenderSystem:
    """
    Dibuja una etiqueta con el nombre del estado FSM sobre cada NPC.
    """
    def __init__(self):
        self.font = pygame.font.SysFont(None, 14)

    def update(self, world, screen, camera):
        comps = world.components
        for eid in world.get_entities_with('NPCState', 'Position', 'Sprite'):
            state_name = comps['NPCState'][eid].fsm.current_state.__class__.__name__
            pos = comps['Position'][eid]
            sprite_cmp = comps['Sprite'][eid]
            w, h = sprite_cmp.image.get_size()
            # calcular posición en pantalla
            x = (pos.x - camera.offset_x + w/2) * camera.zoom
            y = (pos.y - camera.offset_y) * camera.zoom
            label = self.font.render(state_name, True, (255, 255, 255))
            lw, lh = label.get_size()
            screen.blit(label, (x - lw/2, y - lh))