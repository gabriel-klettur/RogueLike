# Path: src/roguelike_engine/input/mouse.py
import pygame

def handle_mouse(event, state, camera, clock, map):
    
    if event.type == pygame.MOUSEWHEEL:
        if event.y > 0: camera.zoom = min(camera.zoom + 0.1, 2.0)
        else:          camera.zoom = max(camera.zoom - 0.1, 0.5)

    elif event.type == pygame.MOUSEBUTTONDOWN:
        if event.button == 1:
            _click_left(state, camera, map)
        elif event.button == 2:
            state.systems.effects.shooting_laser = True
            state.systems.effects.last_laser_time = 0
        elif event.button == 3:
            _click_right(state, camera)

    elif event.type == pygame.MOUSEBUTTONUP and event.button == 2:
        state.systems.effects.shooting_laser = False

def _click_left(state, camera, map):
    mx,my = pygame.mouse.get_pos()
    wx = mx/camera.zoom + camera.offset_x
    wy = my/camera.zoom + camera.offset_y

    px = state.player.x + state.player.sprite_size[0]/2
    py = state.player.y + state.player.sprite_size[1]/2

    dx,dy = wx-px, wy-py
    angle = -pygame.math.Vector2(dx, dy).angle_to((1,0))
    state.systems.effects.spawn_fireball(angle, map)

def _click_right(state, camera):
    mx,my = pygame.mouse.get_pos()
    wx = mx/camera.zoom + camera.offset_x
    wy = my/camera.zoom + camera.offset_y

    state.player.movement.teleport(wx, wy)
    state.systems.effects.spawn_teleport(wx, wy)