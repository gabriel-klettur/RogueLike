# Path: src/roguelike_game/ecs/utils/render_utils.py
import pygame

def draw_sprite_bbox(screen, camera, pos, sprite, color=(255, 0, 0), width=1, scale=1.0):
    """
    Dibuja un rectángulo alrededor del sprite de una entidad, considerando escala y zoom de cámara.
    """
    w, h = sprite.image.get_size()
    sx = (pos.x - camera.offset_x) * camera.zoom
    sy = (pos.y - camera.offset_y) * camera.zoom
    sw = w * scale * camera.zoom
    sh = h * scale * camera.zoom
    rect = pygame.Rect(sx, sy, sw, sh)
    pygame.draw.rect(screen, color, rect, width)
    return rect


def draw_sprite_center(screen, camera, pos, sprite, color=(0, 255, 0), radius=3, scale=1.0):
    """
    Dibuja un círculo en el centro del sprite de una entidad, considerando escala y zoom de cámara.
    """
    w, h = sprite.image.get_size()
    cx = (pos.x + (w * scale) / 2 - camera.offset_x) * camera.zoom
    cy = (pos.y + (h * scale) / 2 - camera.offset_y) * camera.zoom
    pygame.draw.circle(screen, color, (int(cx), int(cy)), radius)
    return (int(cx), int(cy))