# Path: src/roguelike_engine/input/mouse.py
import pygame

def handle_mouse(event, state, camera, clock, map, entities, effects, explosions):
    
    if event.type == pygame.MOUSEWHEEL:
        if event.y > 0: camera.zoom = min(camera.zoom + 0.1, 2.0)
        else:          camera.zoom = max(camera.zoom - 0.1, 0.5)

    elif event.type == pygame.MOUSEBUTTONDOWN:
        if event.button == 1:
            _click_left(state, camera, map, entities, effects, explosions)
        elif event.button == 2:
            effects.shooting_laser = True
            effects.last_laser_time = 0
        elif event.button == 3:
            _click_right(state, camera, entities, effects)

    elif event.type == pygame.MOUSEBUTTONUP and event.button == 2:
        effects.shooting_laser = False

def _click_left(state, camera, map, entities, effects, explosions):
    mx,my = pygame.mouse.get_pos()
    wx = mx/camera.zoom + camera.offset_x
    wy = my/camera.zoom + camera.offset_y

    px = entities.player.x + entities.player.sprite_size[0]/2
    py = entities.player.y + entities.player.sprite_size[1]/2

    dx,dy = wx-px, wy-py
    angle = -pygame.math.Vector2(dx, dy).angle_to((1,0))
    effects.spawn_fireball(angle, map, entities, explosions)

def _click_right(state, camera, entities, effects):
    mx,my = pygame.mouse.get_pos()
    wx = mx/camera.zoom + camera.offset_x
    wy = my/camera.zoom + camera.offset_y

    entities.player.movement.teleport(wx, wy)
    effects.spawn_teleport(wx, wy, entities)