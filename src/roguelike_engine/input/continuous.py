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
        # Cache combined map and building collision tiles
        if not hasattr(entities, '_collision_tiles_cache'):
            bt = []
            for b in entities.buildings:
                bt.extend(b.collision_tile_objs)
            entities._collision_tiles_cache = list(map.solid_tiles) + bt
        solid_tiles = entities._collision_tiles_cache
        entities.player.move(dx, dy, solid_tiles, entities.obstacles)
    
    if effects.shooting_laser:
        now = time.time()
        if now - effects.last_laser_time >= 0.01:
            mx,my = pygame.mouse.get_pos()
            wx = mx/camera.zoom + camera.offset_x
            wy = my/camera.zoom + camera.offset_y            
            effects.spawn_laser(wx, wy, entities)
            effects.last_laser_time = now