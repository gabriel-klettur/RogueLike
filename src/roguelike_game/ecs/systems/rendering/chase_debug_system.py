import pygame
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_game.ecs.systems.ai.chase_system import ChaseSystem
from roguelike_game.ecs.utils.position_utils import compute_foot_tile
from roguelike_game.ecs.utils.render_utils import draw_sprite_bbox

class ChaseDebugSystem:
    """
    Dibuja debug de ChaseSystem:
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
        # Obtener centros usando ChaseSystem
        center_x, center_y, origins = ChaseSystem.compute_centers(world)
        if origins is None or not origins:
            return
        scx = (center_x - camera.offset_x) * camera.zoom
        scy = (center_y - camera.offset_y) * camera.zoom
        # Dibujar centro del jugador
        pygame.draw.circle(screen, (0,255,0), (int(scx), int(scy)), 4)
        # Tile centrado en los pies del jugador
        for pid in comps.get('PlayerTagComponent', {}):
            tile_coords = compute_foot_tile(world, pid, TILE_SIZE)
            if tile_coords:
                tx, ty = tile_coords
                ts = TILE_SIZE * camera.zoom
                tsx = (tx * TILE_SIZE - camera.offset_x) * camera.zoom
                tsy = (ty * TILE_SIZE - camera.offset_y) * camera.zoom
                pygame.draw.rect(screen, (0,0,255), pygame.Rect(tsx, tsy, ts, ts), 1)

        # Para cada NPC con ChaseTarget
        for eid, (origin_x, origin_y) in origins.items():
            pos = comps.get('Position', {}).get(eid)
            sprite = comps.get('Sprite', {}).get(eid)
            if not pos or not sprite:
                continue
            # Centro del NPC
            sox = (origin_x - camera.offset_x) * camera.zoom
            soy = (origin_y - camera.offset_y) * camera.zoom
            pygame.draw.circle(screen, (255,0,255), (int(sox), int(soy)), 4)
            # Línea hacia centro del jugador
            pygame.draw.line(screen, (0,0,255), (int(sox), int(soy)), (int(scx), int(scy)), 1)
            # Bounding box del NPC en amarillo
            scale_cmp = comps.get('Scale', {}).get(eid)
            entity_scale = scale_cmp.scale if scale_cmp else 1.0
            draw_sprite_bbox(screen, camera, pos, sprite, color=(255,255,0), width=1, scale=entity_scale)
            # Tile centrado en los pies del NPC
            tile_coords = compute_foot_tile(world, eid, TILE_SIZE)
            if tile_coords:
                tx, ty = tile_coords
                ts = TILE_SIZE * camera.zoom
                tsx = (tx * TILE_SIZE - camera.offset_x) * camera.zoom
                tsy = (ty * TILE_SIZE - camera.offset_y) * camera.zoom
                pygame.draw.rect(screen, (255,0,0), pygame.Rect(tsx, tsy, ts, ts), 1)
