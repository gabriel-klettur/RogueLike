import pygame
import os
import logging
from typing import Optional, Dict
from roguelike_engine.config.config import ASSETS_DIR
_IMAGE_CACHE = {}
logger = logging.getLogger(__name__)

# Cache to speed up basename -> absolute path remaps per top-level folder
_ASSET_REMAP_CACHE: Dict[str, Dict[str, Optional[str]]] = {}
# Cache to remember full legacy rel paths (e.g., "items/foo.png") -> resolved absolute path
_PATH_REMAP_CACHE: Dict[str, str] = {}

def _find_asset_by_basename(top_dir: str, basename: str) -> Optional[str]:
    """
    Busca de forma perezosa un archivo por su basename (insensible a mayúsculas)
    dentro de ASSETS_DIR/top_dir y cachea el primer match.
    Devuelve la ruta absoluta si encuentra algo, o None.
    """
    root = os.path.join(ASSETS_DIR, top_dir)
    if not os.path.isdir(root):
        return None
    cache = _ASSET_REMAP_CACHE.setdefault(top_dir, {})
    key = basename.lower()
    if key in cache:
        return cache[key]
    for dirpath, _dirnames, filenames in os.walk(root):
        for fname in filenames:
            if fname.lower() == key:
                found = os.path.join(dirpath, fname)
                cache[key] = found
                return found
    # Cache negative lookups to avoid repeated walks
    cache[key] = None  # type: ignore[assignment]
    return None

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

    # Fallback: si la ruta no existe y el recurso estaba bajo 'items',
    # buscar por basename dentro de subcarpetas de assets/items.
    if not os.path.isfile(full_path):
        try:
            # Primero, revisar si ya mapeamos esta ruta relativa previamente
            mapped = _PATH_REMAP_CACHE.get(rel)
            if mapped and os.path.isfile(mapped):
                full_path = mapped
            else:
                # Determinar carpeta top-level del path relativo (sin prefijo assets/)
                rel_no_assets = rel[7:] if rel.startswith("assets/") else rel
                parts = [p for p in rel_no_assets.split("/") if p]
                if "items" in parts:
                    basename = os.path.basename(full_path)
                    candidate = _find_asset_by_basename("items", basename)
                    if candidate and os.path.isfile(candidate):
                        # Guardar el remapeo para evitar nuevos walks y warnings
                        _PATH_REMAP_CACHE[rel] = candidate
                        logger.warning(
                            f"[loader.load_image] Remapeado recurso movido '{full_path}' -> '{candidate}'"
                        )
                        full_path = candidate
        except Exception as e:
            logger.debug(f"[loader.load_image] Fallback remap error: {e}")

    # Cache images to avoid redundant I/O
    key = (full_path, scale)
    if key in _IMAGE_CACHE:
        return _IMAGE_CACHE[key]

    if not os.path.isfile(full_path):
        base = os.path.basename(full_path).lower()
        if base == "dummy.png":
            try:
                w, h = (scale if scale else (32, 32))
            except Exception:
                w, h = (32, 32)
            img = pygame.Surface((w, h), pygame.SRCALPHA)
            img.fill((255, 0, 255, 255))
            try:
                pygame.draw.line(img, (0, 0, 0), (0, 0), (w - 1, h - 1), 2)
                pygame.draw.line(img, (0, 0, 0), (0, h - 1), (w - 1, 0), 2)
            except Exception:
                pass
            _IMAGE_CACHE[key] = img
            return img
        logger.error(f"[loader.load_image] Imagen no encontrada: '{full_path}' (desde path='{path}')")
        raise FileNotFoundError(f"Imagen no encontrada: {full_path}")

    try:
        img = pygame.image.load(full_path).convert_alpha()
    except Exception as e:
        # Manejar PNG corrupto u otros errores de lectura sin crashear el juego
        logger.error(f"[loader.load_image] Error cargando PNG: '{full_path}': {e}")
        # Crear un placeholder visible (magenta con cruces negras)
        try:
            w, h = (scale if scale else (32, 32))
        except Exception:
            w, h = (32, 32)
        img = pygame.Surface((w, h), pygame.SRCALPHA)
        img.fill((255, 0, 255, 255))
        try:
            pygame.draw.line(img, (0, 0, 0), (0, 0), (w - 1, h - 1), 2)
            pygame.draw.line(img, (0, 0, 0), (0, h - 1), (w - 1, 0), 2)
        except Exception:
            pass
    if scale and img.get_size() != scale:
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