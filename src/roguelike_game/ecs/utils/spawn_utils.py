"""spawn_utils.py: Utilities for entity spawning with BFS search.

This module provides functions to build the feet collider rectangle, perform BFS
search over map tiles to find valid spawn positions, and track all computed BFS
paths for debugging purposes.
"""

import pygame
from roguelike_engine.config.config_tiles import TILE_SIZE
from collections import deque
import roguelike_engine.config.config as config
from roguelike_engine.config.map_config import global_map_settings

# Global list storing all BFS paths computed during spawn operations for debugging.
spawn_paths: list[list[tuple[int,int]]] = []

def _build_feet_rect(tx: int, ty: int, w: int, h: int, off_x: int, off_y: int,
                     margin: int) -> pygame.Rect:
    """
    Build the rectangle representing the feet collider for a sprite at a given tile position.

    Args:
        tx (int): X coordinate of the tile.
        ty (int): Y coordinate of the tile.
        w (int): Width of the sprite in pixels.
        h (int): Height of the sprite in pixels.
        off_x (int): Horizontal offset of the collider relative to sprite center.
        off_y (int): Vertical offset of the collider relative to sprite bottom.
        margin (int): Additional margin around the collider in tile units.

    Returns:
        pygame.Rect: Feet collider rectangle in world coordinates.
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
               max_radius: int, margin: int,
               npc_id: int | None = None) -> tuple[int, int] | None:
    """
    Perform breadth-first search (BFS) from an origin tile to find the nearest valid spawn position.

    Args:
        map_manager: Provides access to solid tile rectangles for collision checks.
        orig_x (int): Starting tile X position.
        orig_y (int): Starting tile Y position.
        w (int): Width of the sprite in pixels.
        h (int): Height of the sprite in pixels.
        off_x (int): Horizontal offset of the feet collider relative to sprite.
        off_y (int): Vertical offset of the feet collider relative to sprite.
        max_radius (int): Maximum search radius in tiles.
        margin (int): Tile margin around the feet collider for collision buffer.
        npc_id (int | None): ID of the NPC being spawned (optional).

    Returns:
        tuple[int, int] | None: First valid spawn tile coordinates, or None if none found.
    """    
    print(f"[ECS][Spawn][BFS] NPC: {npc_id} buscando spawn en ({orig_x}, {orig_y})")
    # parent mapping para reconstruir el path
    parents: dict[tuple[int,int], tuple[int,int] | None] = {}
    parents[(orig_x, orig_y)] = None
    visited = {(orig_x, orig_y)}
    # calcular área válida de spawn: lobby
    lob_x, lob_y = map_manager.lobby_offset
    zone_w, zone_h = global_map_settings.zone_size
    lobby_rect = pygame.Rect(
        lob_x * TILE_SIZE,
        lob_y * TILE_SIZE,
        zone_w * TILE_SIZE,
        zone_h * TILE_SIZE,
    )
    q = deque([(orig_x, orig_y, 0)])
    while q:
        tx, ty, dist = q.popleft()
        rect = _build_feet_rect(tx, ty, w, h, off_x, off_y, margin)
        # descartar posiciones fuera del lobby
        if not lobby_rect.contains(rect):
            if config.DEBUG:
                print(f"[ECS][Spawn][BFS] NPC {npc_id} feet rect {rect} fuera del lobby {lobby_rect}, ignorar")
            continue
        # sin colisión con tiles sólidos
        if not any(rect.colliderect(tile.rect) for tile in map_manager.solid_tiles):
            # reconstruir path desde origen a (tx,ty)
            path: list[tuple[int,int]] = []
            node = (tx, ty)
            while node is not None:
                path.append(node)
                node = parents.get(node)
            path.reverse()
            # almacenar path para debug de spawn
            spawn_paths.append(path)
            if config.DEBUG:
                print(f"[ECS][Spawn][BFS] NPC {npc_id} BFS spawn path from {(orig_x, orig_y)} to {(tx, ty)}: {path}")
            return tx, ty
        if dist < max_radius:
            for dx, dy in ((1,0),(-1,0),(0,1),(0,-1)):
                nx, ny = tx+dx, ty+dy
                if (nx, ny) not in visited:
                    visited.add((nx, ny))
                    parents[(nx, ny)] = (tx, ty)
                    q.append((nx, ny, dist+1))
        
    print(f"[ECS][Spawn][BFS] NPC {npc_id} no se encontró spawn válido")
    return None

def find_valid_spawn(map_manager, cx: int, cy: int, sprite, scale: float = 1.0,
                     max_radius: int = 5, margin_tiles: int = 1,
                     npc_id: int | None = None) -> tuple[int, int]:
    """
    Find a valid spawn location for a sprite using BFS search with optional margin.

    This function first attempts search with a specified tile margin and retries without
    margin if no valid position is found within the given radius.

    Args:
        map_manager: Provides access to solid tile rectangles for collision checks.
        cx (int): Desired spawn tile X position.
        cy (int): Desired spawn tile Y position.
        sprite: Sprite instance for width/height calculations.
        scale (float): Scale factor applied to the sprite (default 1.0).
        max_radius (int): Maximum search radius in tiles (default 5).
        margin_tiles (int): Tile margin for initial collision buffer (default 1).
        npc_id (int | None): ID of the NPC being spawned (optional).

    Returns:
        tuple[int, int]: Coordinates of the valid spawn tile found.
    """
    w = int(sprite.image.get_width() * scale)
    h = int(sprite.image.get_height() * scale)
    off_x = (w - w//2) // 2
    off_y = h - int(h * 0.2)

    # Intento con margen
    result = _bfs_spawn(map_manager, cx, cy, w, h, off_x, off_y, max_radius, margin_tiles, npc_id)
    if result:
        return result
    # Segundo intento sin margen
    return _bfs_spawn(map_manager, cx, cy, w, h, off_x, off_y, max_radius, 0, npc_id) or (cx, cy)