import pygame
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_game.ecs.utils.position_utils import compute_foot_tile
from roguelike_game.ecs.utils.render_utils import draw_sprite_bbox
from roguelike_game.ecs.components.transform.scale import Scale

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

    def update(self, world, screen, camera, perf_log=None):
        # Toggle debug mode on F9 (only on key down)
        keys = pygame.key.get_pressed()
        f9 = keys[pygame.K_F9]
        if f9 != self.last_pressed:
            if f9:
                self.debug = not self.debug
            self.last_pressed = f9
        if not self.debug:
            return

        # Cache components and camera parameters
        comps = world.components
        offset_x = camera.offset_x
        offset_y = camera.offset_y
        zoom = camera.zoom
        sw, sh = screen.get_size()
        ts = int(TILE_SIZE * zoom)
        draw_circle = pygame.draw.circle
        draw_line = pygame.draw.line
        draw_rect = pygame.draw.rect

        # Draw player center and foot tile
        player_pos = world.player_position
        if player_pos:
            pid = world.player_entity
            sprite = comps.get('Sprite', {}).get(pid)
            if sprite:
                w, h = sprite.image.get_size()
            else:
                w, h = 0, 0
            cx = player_pos.x + w * 0.5
            cy = player_pos.y + h * 0.5
            scx = int((cx - offset_x) * zoom)
            scy = int((cy - offset_y) * zoom)
            draw_circle(screen, (0, 255, 0), (scx, scy), 4)
            for pid in comps.get('PlayerTagComponent', {}):
                tile = compute_foot_tile(world, pid, TILE_SIZE)
                if tile:
                    tx, ty = tile
                    tsx = int((tx * TILE_SIZE - offset_x) * zoom)
                    tsy = int((ty * TILE_SIZE - offset_y) * zoom)
                    draw_rect(screen, (0, 0, 255), pygame.Rect(tsx, tsy, ts, ts), 1)

        # Draw debug for NPCs with frustum culling
        positions = comps.get('Position', {})
        sprites = comps.get('Sprite', {})
        scales = comps.get('Scale', {})
        player_center = (scx, scy) if player_pos else None
        for eid in world.get_entities_with('NPCState', 'Position', 'Sprite'):
            pos = positions[eid]
            sprite = sprites[eid]
            w, h = sprite.image.get_size()
            scale_val = scales.get(eid).scale if eid in scales else 1.0
            w *= scale_val
            h *= scale_val
            cx_npc = pos.x + w * 0.5
            cy_npc = pos.y + h * 0.5
            sox = int((cx_npc - offset_x) * zoom)
            soy = int((cy_npc - offset_y) * zoom)
            # Skip off-screen
            if sox < -4 or sox > sw + 4 or soy < -4 or soy > sh + 4:
                continue
            draw_circle(screen, (255, 0, 255), (sox, soy), 4)
            if player_center:
                draw_line(screen, (0, 0, 255), (sox, soy), player_center, 1)
            draw_sprite_bbox(screen, camera, pos, sprite, color=(255, 255, 0), width=1, scale=scale_val)
            tile = compute_foot_tile(world, eid, TILE_SIZE)
            if tile:
                tx, ty = tile
                tsx = int((tx * TILE_SIZE - offset_x) * zoom)
                tsy = int((ty * TILE_SIZE - offset_y) * zoom)
                draw_rect(screen, (255, 0, 0), pygame.Rect(tsx, tsy, ts, ts), 1)
