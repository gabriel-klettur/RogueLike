# Path: src/roguelike_engine/utils/loader.py
import pygame
import os
from roguelike_engine.config.config import ASSETS_DIR

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

    if not os.path.isfile(full_path):
        raise FileNotFoundError(f"Imagen no encontrada: {full_path}")

    img = pygame.image.load(full_path).convert_alpha()
    if scale:
        img = pygame.transform.scale(img, scale)
    return img

def load_sprite_sheet(path: str, sprite_size: tuple[int,int],
                      row=0, columns=1, start_col=0) -> list[pygame.Surface]:
    """
    Igual que load_image, pero corta el sheet en frames.
    path = "characters/first_hero/first_hero.png", etc.
    """
    sheet = load_image(path)
    w, h = sprite_size
    frames = []
    for col in range(start_col, start_col + columns):
        rect = pygame.Rect(col * w, row * h, w, h)
        frames.append(sheet.subsurface(rect).copy())
    return frames

def load_explosion_frames(path_fmt: str, count: int, scale=None):
    """
    path_fmt: p.ej. "explosions/explosion_{0}.png"
    """
    frames = []
    for i in range(count):
        frames.append(load_image(path_fmt.format(i), scale))
    return frames