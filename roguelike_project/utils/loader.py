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
