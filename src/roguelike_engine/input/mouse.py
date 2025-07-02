# Path: src/roguelike_engine/input/mouse.py
import pygame

def handle_mouse(event, state, camera, clock, map, entities):
    
    if event.type == pygame.MOUSEWHEEL:
        if event.y > 0: camera.zoom = min(camera.zoom + 0.1, 2.0)
        else:          camera.zoom = max(camera.zoom - 0.1, 0.5)
    elif event.type == pygame.MOUSEBUTTONDOWN:
        if event.button == 3:
            # Right-click dash handled by ECS InputSystem; no legacy action
            pass