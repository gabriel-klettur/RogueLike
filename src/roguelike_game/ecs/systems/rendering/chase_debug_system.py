import pygame
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_game.ecs.fsm.states.chase_state import ChaseState
from roguelike_game.ecs.fsm.states.aggro_state import AggroState
from roguelike_game.ecs.utils.position_utils import compute_foot_tile
from roguelike_game.ecs.utils.render_utils import draw_sprite_bbox

#! DEBERIAMOS IMPLEMENTARLO DENTRO DE NUESTRO FSM

class ChaseDebugSystem:
    """
    Dibuja debug de ChaseState:
    - Centro del jugador en verde
    - Centro de cada NPC en magenta
    - Línea desde NPC al destino en azul
    """
    def __init__(self):
        self.debug = False
        self.last_pressed = False

    def update(self, world, screen, camera):
        # Toggle debug mode on F9
        keys = pygame.key.get_pressed()
        f9 = keys[pygame.K_F9]
        if f9 and not self.last_pressed:
            self.debug = not self.debug
        self.last_pressed = f9
        if not self.debug:
            return

        comps = world.components
        # Dibujar centro del jugador
        player_pos = world.player_position
        if player_pos:
            # Calcular centro de sprite del jugador
            player_id = world.player_entity
            sprite_cmp = comps.get('Sprite', {}).get(player_id)
            if sprite_cmp:
                w, h = sprite_cmp.image.get_size()
            else:
                w, h = 0, 0
            cx = player_pos.x + w / 2
            cy = player_pos.y + h / 2
            scx = (cx - camera.offset_x) * camera.zoom
            scy = (cy - camera.offset_y) * camera.zoom
            pygame.draw.circle(screen, (0,255,0), (int(scx), int(scy)), 4)
            # Tile de pies del jugador
            for pid in comps.get('PlayerTagComponent', {}):
                tile_coords = compute_foot_tile(world, pid, TILE_SIZE)
                if tile_coords:
                    tx, ty = tile_coords
                    ts = TILE_SIZE * camera.zoom
                    tsx = (tx * TILE_SIZE - camera.offset_x) * camera.zoom
                    tsy = (ty * TILE_SIZE - camera.offset_y) * camera.zoom
                    pygame.draw.rect(screen, (0,0,255), pygame.Rect(tsx, tsy, ts, ts), 1)

        # Para cada NPC en ChaseState o AggroState
        for eid in world.get_entities_with('NPCState', 'Position', 'Sprite'):
            state = comps['NPCState'][eid].fsm.current_state
            # Considerar AggroState también como chase
            if not isinstance(state, (ChaseState, AggroState)):
                continue
            pos = comps['Position'][eid]
            sprite = comps['Sprite'][eid]
            # Centro del NPC
            sox = (pos.x - camera.offset_x) * camera.zoom
            soy = (pos.y - camera.offset_y) * camera.zoom
            pygame.draw.circle(screen, (255,0,255), (int(sox), int(soy)), 4)
            # Línea hacia jugador
            if player_pos:
                # Trazar línea desde NPC al centro de sprite del jugador
                pygame.draw.line(screen, (0,0,255), (int(sox), int(soy)), (int(scx), int(scy)), 1)
            # Bounding box del NPC
            scale_cmp = comps.get('Scale', {}).get(eid)
            entity_scale = scale_cmp.scale if scale_cmp else 1.0
            draw_sprite_bbox(screen, camera, pos, sprite, color=(255,255,0), width=1, scale=entity_scale)
            # Tile de pies del NPC
            tile_coords = compute_foot_tile(world, eid, TILE_SIZE)
            if tile_coords:
                tx, ty = tile_coords
                ts = TILE_SIZE * camera.zoom
                tsx = (tx * TILE_SIZE - camera.offset_x) * camera.zoom
                tsy = (ty * TILE_SIZE - camera.offset_y) * camera.zoom
                pygame.draw.rect(screen, (255,0,0), pygame.Rect(tsx, tsy, ts, ts), 1)
