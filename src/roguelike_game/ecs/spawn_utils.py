import pygame
from roguelike_engine.config.config_tiles import TILE_SIZE
from collections import deque

def find_valid_spawn(map_manager, cx, cy, sprite, scale: float = 1.0,
                     max_radius: int = 5, margin_tiles: int = 1) -> tuple[int, int]:
    """
    Busca la celda más cercana a (cx, cy) donde el "feet"-collider de "sprite" cabe sin colisionar.

    Args:
        map_manager: Objeto con solid_tiles (lista de pygame.Rect).
        cx, cy: Coordenadas del tile inicial.
        sprite: Componente Sprite con atributo .image.
        scale: Factor de escalado para .image.
        max_radius: Máxima distancia de búsqueda en tiles.
        margin_tiles: Margen extra en tiles alrededor del feet-collider.

    Returns:
        (tx, ty): Coordenadas del tile válido más cercano, o (cx, cy) si no se encuentra otro.
    """
    orig_x, orig_y = cx, cy
    w = int(sprite.image.get_width() * scale)
    h = int(sprite.image.get_height() * scale)
    feet_w = w // 2
    feet_h = int(h * 0.2)
    off_x = (w - feet_w) // 2
    off_y = h - feet_h

    def bfs(margin):
        visited = {(orig_x, orig_y)}
        q = deque([(orig_x, orig_y, 0)])
        while q:
            tx, ty, dist = q.popleft()
            px = tx * TILE_SIZE - w // 2 + off_x
            py = ty * TILE_SIZE - h // 2 + off_y
            # Construye rect del feet-collider con margen
            rect = pygame.Rect(
                px - margin * TILE_SIZE,
                py - margin * TILE_SIZE,
                feet_w + margin * 2 * TILE_SIZE,
                feet_h + margin * 2 * TILE_SIZE
            )
            # Si no colisiona con sólido, retorno coords
            if not any(rect.colliderect(t.rect) for t in map_manager.solid_tiles):
                return tx, ty
            if dist < max_radius:
                for dx, dy in ((1,0),(-1,0),(0,1),(0,-1)):
                    nx, ny = tx + dx, ty + dy
                    if (nx, ny) not in visited:
                        visited.add((nx, ny))
                        q.append((nx, ny, dist + 1))
        return None

    result = bfs(margin_tiles)
    return result or bfs(0) or (orig_x, orig_y)
