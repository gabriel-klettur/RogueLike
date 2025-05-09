# Path: src/roguelike_engine/input/continuous.py
import pygame, time

def handle_continuous(state, camera, map, entities, menu, effects):
    # Movimiento continuo
    if not menu.show_menu:
        keys = pygame.key.get_pressed()
        dx = (keys[pygame.K_RIGHT] or keys[pygame.K_d]) - (keys[pygame.K_LEFT] or keys[pygame.K_a])
        dy = (keys[pygame.K_DOWN]  or keys[pygame.K_s]) - (keys[pygame.K_UP]   or keys[pygame.K_w])
        entities.player.is_walking = bool(dx or dy)
        solid = [t for t in map.tiles_in_region if t.solid]
        entities.player.move(dx, dy, entities.obstacles, solid)
    
    if effects.shooting_laser:
        now = time.time()
        if now - effects.last_laser_time >= 0.01:
            mx,my = pygame.mouse.get_pos()
            wx = mx/camera.zoom + camera.offset_x
            wy = my/camera.zoom + camera.offset_y
            enemies = entities.enemies
            effects.spawn_laser(wx, wy, enemies, entities)
            effects.last_laser_time = now