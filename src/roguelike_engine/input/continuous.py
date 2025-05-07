# Path: src/roguelike_engine/input/continuous.py
import pygame, time

def handle_continuous(state, camera, map):
    # Movimiento continuo
    if not state.show_menu:
        keys = pygame.key.get_pressed()
        dx = (keys[pygame.K_RIGHT] or keys[pygame.K_d]) - (keys[pygame.K_LEFT] or keys[pygame.K_a])
        dy = (keys[pygame.K_DOWN]  or keys[pygame.K_s]) - (keys[pygame.K_UP]   or keys[pygame.K_w])
        state.player.is_walking = bool(dx or dy)
        solid = [t for t in map.tiles_in_region if t.solid]
        state.player.move(dx, dy, state.obstacles, solid)

    # Laser continuo
    effects = state.systems.effects
    if effects.shooting_laser:
        now = time.time()
        if now - effects.last_laser_time >= 0.01:
            mx,my = pygame.mouse.get_pos()
            wx = mx/camera.zoom + camera.offset_x
            wy = my/camera.zoom + camera.offset_y
            enemies = state.enemies + list(state.remote_entities.values())
            effects.spawn_laser(wx, wy, enemies)
            effects.last_laser_time = now