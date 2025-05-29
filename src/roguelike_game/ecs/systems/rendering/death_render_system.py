import pygame
import time
from roguelike_game.ecs.fsm.states.death_state import DeathState

class DeathRenderSystem:
    """
    Dibuja el sprite de muerte y la barra de temporizador para NPCs en estado DeathState.
    """
    def __init__(self, bar_height: int = 5, offset: int = 2,
                 color_bg: tuple = (50, 50, 50), color_fg: tuple = (255, 0, 0)):
        self.bar_height = bar_height
        self.offset = offset
        self.color_bg = color_bg
        self.color_fg = color_fg

    def update(self, world, screen, camera):
        now = time.time()
        # Iterar sobre todos los NPCState
        for eid in list(world.get_entities_with('NPCState')):
            npc_state = world.components['NPCState'][eid]
            if isinstance(npc_state.fsm.current_state, DeathState):
                sprite = world.components['Sprite'].get(eid)
                pos = world.components['Position'].get(eid)
                # Renderizar sprite de muerte
                if sprite and pos and hasattr(sprite, 'death_image'):
                    death_img = sprite.death_image
                    sx, sy = camera.apply((pos.x, pos.y))
                    screen.blit(death_img, (sx, sy))
                # Renderizar barra de temporizador
                dt_cmp = world.components['DeathTimer'].get(eid)
                if sprite and pos and dt_cmp:
                    ratio = max(0.0, (dt_cmp.duration - (now - dt_cmp.start_time)) / dt_cmp.duration)
                    width = sprite.image.get_width()
                    # Aplicar escala si existe
                    scale_cmp = world.components['Scale'].get(eid)
                    scale = scale_cmp.scale if scale_cmp else 1.0
                    width = int(width * scale)
                    sx, sy = camera.apply((pos.x, pos.y))
                    y = sy - self.offset - self.bar_height
                    # Fondo y frente
                    bg_rect = pygame.Rect(sx, y, width, self.bar_height)
                    fg_rect = pygame.Rect(sx, y, int(width * ratio), self.bar_height)
                    pygame.draw.rect(screen, self.color_bg, bg_rect)
                    pygame.draw.rect(screen, self.color_fg, fg_rect)
