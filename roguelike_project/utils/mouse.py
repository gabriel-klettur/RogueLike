import pygame

def get_direction_from_angle(angle):
    if 315 <= angle or angle < 45:
        return "up"
    elif 45 <= angle < 135:
        return "left"
    elif 135 <= angle < 225:
        return "down"
    elif 225 <= angle < 315:
        return "right"

def draw_mouse_crosshair(screen, camera):
    
    # Obtener posición del mouse en coordenadas de mundo
    mouse_x, mouse_y = pygame.mouse.get_pos()
    world_mouse_x = mouse_x / camera.zoom + camera.offset_x
    world_mouse_y = mouse_y / camera.zoom + camera.offset_y

    screen_x, screen_y = camera.apply((world_mouse_x, world_mouse_y))

    color = (255, 0, 0)
    size = 10
    thickness = 2

    pygame.draw.line(screen, color, (screen_x - size, screen_y), (screen_x + size, screen_y), thickness)
    pygame.draw.line(screen, color, (screen_x, screen_y - size), (screen_x, screen_y + size), thickness)
