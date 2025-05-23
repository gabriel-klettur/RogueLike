# Path: src/roguelike_engine/input/continuous.py
import pygame, time
import types
from roguelike_engine.config.config_tiles import TILE_SIZE

def handle_continuous(state, camera, map, entities, menu, effects):
    # Movimiento continuo
    if not menu.show_menu:
        keys = pygame.key.get_pressed()
        dx = (keys[pygame.K_RIGHT] or keys[pygame.K_d]) - (keys[pygame.K_LEFT] or keys[pygame.K_a])
        dy = (keys[pygame.K_DOWN]  or keys[pygame.K_s]) - (keys[pygame.K_UP]   or keys[pygame.K_w])
        entities.player.is_walking = bool(dx or dy)
        solid = map.solid_tiles
        # Incluir colisiones de buildings
        bt_tiles = []
        for b in entities.buildings:
            for ry, row in enumerate(b.collision_map):
                for cx, ch in enumerate(row):
                    if ch == '#':
                        rect = pygame.Rect(b.x + cx * TILE_SIZE, b.y + ry * TILE_SIZE, TILE_SIZE, TILE_SIZE)
                        bt_tiles.append(types.SimpleNamespace(solid=True, rect=rect))
        solid_tiles = list(solid) + bt_tiles
        entities.player.move(dx, dy, solid_tiles, entities.obstacles)
    
    if effects.shooting_laser:
        now = time.time()
        if now - effects.last_laser_time >= 0.01:
            mx,my = pygame.mouse.get_pos()
            wx = mx/camera.zoom + camera.offset_x
            wy = my/camera.zoom + camera.offset_y            
            effects.spawn_laser(wx, wy, entities)
            effects.last_laser_time = now