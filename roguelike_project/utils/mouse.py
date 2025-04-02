import pygame

def get_direction_from_angle(angle):
    if -22.5 <= angle < 22.5:
        return "up"
    elif 22.5 <= angle < 67.5:
        return "up_right"
    elif 67.5 <= angle < 112.5:
        return "right"
    elif 112.5 <= angle < 157.5:
        return "down_right"
    elif angle >= 157.5 or angle < -157.5:
        return "down"
    elif -157.5 <= angle < -112.5:
        return "down_left"
    elif -112.5 <= angle < -67.5:
        return "left"
    elif -67.5 <= angle < -22.5:
        return "up_left"
    return "down"

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
