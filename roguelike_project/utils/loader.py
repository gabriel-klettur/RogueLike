import pygame
import os

def load_image(path, scale=None):
    # ✅ Ruta base absoluta (a partir del archivo actual)
    base_path = os.path.dirname(os.path.abspath(__file__))
    full_path = os.path.join(base_path, "..", path)

    image = pygame.image.load(full_path).convert_alpha()
    if scale:
        image = pygame.transform.scale(image, scale)
    return image

def load_explosion_frames(path_format, count, scale=None):
    return [
        load_image(path_format.format(i), scale)
        for i in range(count)
    ]

def load_sprite_sheet(path, sprite_size, row=0, columns=1, start_col=0):
    sheet = pygame.image.load(path).convert_alpha()
    frame_width, frame_height = sprite_size
    frames = []
    for col in range(start_col, start_col + columns):
        rect = pygame.Rect(col * frame_width, row * frame_height, frame_width, frame_height)
        frame = sheet.subsurface(rect).copy()
        frames.append(frame)
    return frames
