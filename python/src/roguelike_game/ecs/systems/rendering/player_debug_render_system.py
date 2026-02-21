import pygame
from roguelike_game.ecs.utils.render_utils import draw_sprite_bbox, draw_sprite_center
from roguelike_engine.utils.benchmark.benchmark import benchmark

class PlayerDebugRenderSystem:
    """
    Dibuja bounding box y centro del sprite del jugador cuando se presiona F9.
    """
    def __init__(self, perf_log):
        # Estado de toggling de debug
        self.debug = False
        self.last_pressed = False
        self.perf_log = perf_log
        self.font = pygame.font.Font(None, 24)
    
    def update(self, world, screen, camera):
        # Toggle debug mode on F9 press
        keys = pygame.key.get_pressed()
        f9 = keys[pygame.K_F9]
        if f9 and not self.last_pressed:
            self.debug = not self.debug
        self.last_pressed = f9
        if not self.debug:
            return

        # Obtener entidad ECS del jugador
        player_eid = getattr(world, 'player_entity', None)
        if player_eid is None:
            return

        comps = world.components
        sprite = comps.get('Sprite', {}).get(player_eid)
        pos = comps.get('Position', {}).get(player_eid)
        if not sprite or not pos:
            return

        # Dibuja bounding box y marcador de centro usando utilidades
        bbox = draw_sprite_bbox(screen, camera, pos, sprite)
        cx, cy = draw_sprite_center(screen, camera, pos, sprite)
        # Debug: dibujar destino de NPCs (chase target) en azul
        pygame.draw.circle(screen, (0, 0, 255), (cx, cy), 6, 1)
        # Debug: dibujar estado FSM del jugador
        state_cmp = comps.get('NPCState', {}).get(player_eid)
        state_name = state_cmp.fsm.current_state.__class__.__name__ if state_cmp else 'NoState'
        label = self.font.render(state_name, True, (255, 255, 255))
        lw, lh = label.get_size()
        screen.blit(label, (bbox.x + bbox.width/2 - lw/2, bbox.y - lh - 2))