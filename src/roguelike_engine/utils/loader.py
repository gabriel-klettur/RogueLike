# Path: src/roguelike_engine/utils/loader.py
import pygame
import os
from roguelike_engine.config.config import ASSETS_DIR
_IMAGE_CACHE = {}

def load_image(path: str, scale=None) -> pygame.Surface:
    """
    Carga una imagen desde ASSETS_DIR. 
    path puede venir con o sin prefijo "assets/", p.ej.:
      - "tiles/floor_1.png"
      - "buildings/houses/orden_house.png"
      - "assets/ui/restore_icon.png"
    """
    # Normalizar separadores
    rel = path.replace("\\", "/")
    # Si el usuario pasó "assets/...", lo quitamos
    if rel.startswith("assets/"):
        rel = rel[len("assets/"):]
    # Construimos la ruta absoluta
    full_path = os.path.join(ASSETS_DIR, *rel.split("/"))

    # Cache images to avoid redundant I/O
    key = (full_path, scale)
    if key in _IMAGE_CACHE:
        return _IMAGE_CACHE[key]

    if not os.path.isfile(full_path):
        raise FileNotFoundError(f"Imagen no encontrada: {full_path}")

    img = pygame.image.load(full_path).convert_alpha()
    if scale:
        img = pygame.transform.scale(img, scale)
    _IMAGE_CACHE[key] = img
    return img

def load_sprite_sheet(path: str, sprite_size: tuple[int,int],
                      row=0, columns=1, start_col=0) -> list[pygame.Surface]:
    """
    Igual que load_image, pero corta el sheet en frames.
    path = "characters/dwarf/dwarf.png", etc.
    """
    sheet = load_image(path)
    w, h = sprite_size
    frames = []
    for col in range(start_col, start_col + columns):
        rect = pygame.Rect(col * w, row * h, w, h)
        frames.append(sheet.subsurface(rect).copy())
    return frames