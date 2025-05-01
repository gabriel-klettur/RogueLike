# Path: src/roguelike_engine/input/mouse.py
import pygame

def handle_mouse(event, state):
    cam = state.camera
    if event.type == pygame.MOUSEWHEEL:
        if event.y > 0: cam.zoom = min(cam.zoom + 0.1, 2.0)
        else:          cam.zoom = max(cam.zoom - 0.1, 0.5)

    elif event.type == pygame.MOUSEBUTTONDOWN:
        if event.button == 1:
            _click_left(state)
        elif event.button == 2:
            state.systems.effects.shooting_laser = True
            state.systems.effects.last_laser_time = 0
        elif event.button == 3:
            _click_right(state)

    elif event.type == pygame.MOUSEBUTTONUP and event.button == 2:
        state.systems.effects.shooting_laser = False

def _click_left(state):
    mx,my = pygame.mouse.get_pos()
    wx = mx/state.camera.zoom + state.camera.offset_x
    wy = my/state.camera.zoom + state.camera.offset_y

    px = state.player.x + state.player.sprite_size[0]/2
    py = state.player.y + state.player.sprite_size[1]/2

    dx,dy = wx-px, wy-py
    angle = -pygame.math.Vector2(dx, dy).angle_to((1,0))
    state.systems.effects.spawn_fireball(angle)

def _click_right(state):
    mx,my = pygame.mouse.get_pos()
    wx = mx/state.camera.zoom + state.camera.offset_x
    wy = my/state.camera.zoom + state.camera.offset_y

    state.player.movement.teleport(wx, wy)
    state.systems.effects.spawn_teleport(wx, wy)