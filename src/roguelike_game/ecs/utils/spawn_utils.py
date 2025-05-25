import pygame
from roguelike_engine.config.config_tiles import TILE_SIZE
from collections import deque

def _build_feet_rect(tx: int, ty: int, w: int, h: int, off_x: int, off_y: int,
                     margin: int) -> pygame.Rect:
    """
    Construye el pygame.Rect para el feet-collider en el tile (tx, ty),
    con offset y margen dado.
    """
    px = tx * TILE_SIZE - w // 2 + off_x
    py = ty * TILE_SIZE - h // 2 + off_y
    return pygame.Rect(
        px - margin * TILE_SIZE,
        py - margin * TILE_SIZE,
        w // 2 + margin * 2 * TILE_SIZE,
        int(h * 0.2) + margin * 2 * TILE_SIZE
    )

def _bfs_spawn(map_manager, orig_x: int, orig_y: int,
               w: int, h: int, off_x: int, off_y: int,
               max_radius: int, margin: int) -> tuple[int, int] | None:
    """
    BFS sobre tiles: busca la primera posición válida
    donde el feet-collider no colisiona con solid_tiles.
    """
    visited = {(orig_x, orig_y)}
    q = deque([(orig_x, orig_y, 0)])
    while q:
        tx, ty, dist = q.popleft()
        rect = _build_feet_rect(tx, ty, w, h, off_x, off_y, margin)
        if not any(rect.colliderect(tile.rect) for tile in map_manager.solid_tiles):
            return tx, ty
        if dist < max_radius:
            for dx, dy in ((1,0),(-1,0),(0,1),(0,-1)):
                nx, ny = tx+dx, ty+dy
                if (nx, ny) not in visited:
                    visited.add((nx, ny))
                    q.append((nx, ny, dist+1))
    return None

def find_valid_spawn(map_manager, cx: int, cy: int, sprite, scale: float = 1.0,
                     max_radius: int = 5, margin_tiles: int = 1) -> tuple[int, int]:
    """
    Punto de entrada: busca el tile válido más cercano.
    Primero con margen, si falla lo intenta sin margen.
    """
    w = int(sprite.image.get_width() * scale)
    h = int(sprite.image.get_height() * scale)
    off_x = (w - w//2) // 2
    off_y = h - int(h * 0.2)

    # Intento con margen
    result = _bfs_spawn(map_manager, cx, cy, w, h, off_x, off_y, max_radius, margin_tiles)
    if result:
        return result
    # Segundo intento sin margen
    return _bfs_spawn(map_manager, cx, cy, w, h, off_x, off_y, max_radius, 0) or (cx, cy)