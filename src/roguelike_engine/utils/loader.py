import pygame
import os
import logging
from roguelike_engine.config.config import ASSETS_DIR
_IMAGE_CACHE = {}
logger = logging.getLogger(__name__)

def load_image(path: str, scale=None) -> pygame.Surface:
    """
    Carga una imagen desde ASSETS_DIR. 
    path puede venir con o sin prefijo "assets/", p.ej.:
      - "tiles/floor_1.png"
      - "buildings/houses/orden_house.png"
      - "assets/ui/restore_icon.png"
    """
    # Normalizar separadores
    rel = path.replace("\\", "/").strip()
    # Soportar rutas absolutas del sistema de archivos (p.ej. D:/.../assets/..)
    if os.path.isabs(rel):
        if os.path.isfile(rel):
            full_path = rel
        else:
            # Intentar re-mapear si el path contiene "/assets/" dentro
            lower = rel.lower()
            marker = "/assets/"
            if marker in lower:
                idx = lower.index(marker) + len(marker)
                rel2 = rel[idx:]
                full_path = os.path.join(ASSETS_DIR, *rel2.split("/"))
                logger.debug(
                    f"[loader.load_image] Remapeando ruta absoluta inexistente '{rel}' -> '{full_path}'"
                )
            else:
                # Ultimo recurso: usar tal cual (fallará más abajo y lo veremos en logs)
                full_path = rel
    else:
        # Si el usuario pasó "assets/...", lo quitamos
        if rel.startswith("assets/"):
            rel = rel[len("assets/") :]
        # Construimos la ruta absoluta relativa a ASSETS_DIR
        full_path = os.path.join(ASSETS_DIR, *rel.split("/"))

    # Cache images to avoid redundant I/O
    key = (full_path, scale)
    if key in _IMAGE_CACHE:
        return _IMAGE_CACHE[key]

    if not os.path.isfile(full_path):
        logger.error(f"[loader.load_image] Imagen no encontrada: '{full_path}' (desde path='{path}')")
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