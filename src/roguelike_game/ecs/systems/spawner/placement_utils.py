"""
Utilities for spawn placement: blocked tiles collection, NPC occupancy, tile iterators,
and unified spot selection honoring shape, spawn_radius and min distance.
"""
from __future__ import annotations

import random
from typing import Iterable, Optional, Set, Tuple

from roguelike_engine.config.config_tiles import TILE_SIZE

Tile = Tuple[int, int]


def collect_blocked_tiles(world) -> tuple[Set[Tile], Set[Tile]]:
    """Return sets for solid tiles and building-collision tiles in global tile coords."""
    solid_coords = {(t.rect.x // TILE_SIZE, t.rect.y // TILE_SIZE) for t in world.map_manager.solid_tiles}
    building_coords = {
        (r.x // TILE_SIZE, r.y // TILE_SIZE)
        for b in getattr(world, 'buildings', [])
        for r in getattr(b, 'collision_tiles', [])
    }
    return solid_coords, building_coords


def collect_npc_tiles(world) -> Set[Tile]:
    """Return set of global tile coords occupied by existing NPCs/Player (alive)."""
    comps = world.components
    death_map = comps.get('DeathTimer', {})
    tiles: Set[Tile] = set()
    for nid in world.get_entities_with('Position', 'MultiCollider'):
        if nid in death_map:
            continue
        p = comps.get('Position', {}).get(nid)
        if not p:
            continue
        tx = int(p.x // TILE_SIZE)
        ty = int(p.y // TILE_SIZE)
        tiles.add((tx, ty))
    return tiles


def iter_spiral_tiles(cx: int, cy: int, max_radius: int) -> Iterable[Tile]:
    """Generate tiles in spiral rings around (cx, cy), starting at center."""
    yield (cx, cy)
    for r in range(1, max_radius + 1):
        x0, x1 = cx - r, cx + r
        y0, y1 = cy - r, cy + r
        # top & bottom edges
        for x in range(x0, x1 + 1):
            yield (x, y0)
            yield (x, y1)
        # left & right edges (without corners to avoid duplicates)
        for y in range(y0 + 1, y1):
            yield (x0, y)
            yield (x1, y)


def _interpret_spawn_radius(spawn_radius_cfg, fallback_max: int) -> tuple[bool, int]:
    """Return (random_mode, radius_tiles) decision from spawn_radius config."""
    random_mode = False
    random_radius = 0
    if isinstance(spawn_radius_cfg, (int, float)):
        random_radius = int(spawn_radius_cfg)
        random_mode = random_radius > 0
    elif isinstance(spawn_radius_cfg, str):
        s = spawn_radius_cfg.strip().lower()
        if s in {"random", "aleatorio", "aleatoreo"}:
            random_mode = True
            random_radius = max(1, int(fallback_max))
    return random_mode, random_radius


def _too_close_px(cx: int, cy: int, min_px_dist_sq: int, tiles: Iterable[Tile]) -> bool:
    for tx, ty in tiles:
        nx = tx * TILE_SIZE + TILE_SIZE // 2
        ny = ty * TILE_SIZE + TILE_SIZE // 2
        ddx = cx - nx
        ddy = cy - ny
        if ddx * ddx + ddy * ddy < min_px_dist_sq:
            return True
    return False


def choose_spawn_tile(
    ax: int,
    ay: int,
    solid: Set[Tile],
    building: Set[Tile],
    npc_tiles: Set[Tile],
    reserved_tiles: Set[Tile],
    reserved_global: Set[Tile],
    map_manager,
    min_px_dist: int,
    fallback_max: int,
    spawn_radius_cfg,
    shape: str,
) -> Optional[Tile]:
    """
    Find a placement tile around anchor (ax, ay) avoiding collisions and respecting optional
    spawn radius and shape. Falls back to spiral search.
    """
    min_px_dist_sq = int(min_px_dist) * int(min_px_dist)
    random_mode, random_radius = _interpret_spawn_radius(spawn_radius_cfg, fallback_max)

    # Try random-in-area first if enabled
    if random_mode:
        square_area = (2 * random_radius + 1) * (2 * random_radius + 1)
        approx_area = square_area if shape == 'square' else max(1, int(square_area * 0.6))
        attempts = max(25, min(200, approx_area))
        for _ in range(attempts):
            dx = random.randint(-random_radius, random_radius)
            dy = random.randint(-random_radius, random_radius)
            if shape != 'square':
                # default circle
                if dx * dx + dy * dy > random_radius * random_radius:
                    continue
            tx = ax + dx
            ty = ay + dy
            if (tx, ty) in solid or (tx, ty) in building:
                continue
            if (tx, ty) in npc_tiles or (tx, ty) in reserved_tiles or (tx, ty) in reserved_global:
                continue
            if map_manager and hasattr(map_manager, 'is_walkable'):
                try:
                    if not map_manager.is_walkable(tx, ty):
                        continue
                except Exception:
                    pass
            if min_px_dist_sq > 0:
                cx = tx * TILE_SIZE + TILE_SIZE // 2
                cy = ty * TILE_SIZE + TILE_SIZE // 2
                if _too_close_px(cx, cy, min_px_dist_sq, npc_tiles):
                    continue
                if _too_close_px(cx, cy, min_px_dist_sq, reserved_tiles.union(reserved_global)):
                    continue
            return (tx, ty)

    # Fallback: center-first spiral
    for tx, ty in iter_spiral_tiles(ax, ay, fallback_max):
        if (tx, ty) in solid or (tx, ty) in building:
            continue
        if (tx, ty) in npc_tiles or (tx, ty) in reserved_tiles or (tx, ty) in reserved_global:
            continue
        if map_manager and hasattr(map_manager, 'is_walkable'):
            try:
                if not map_manager.is_walkable(tx, ty):
                    continue
            except Exception:
                pass
        if min_px_dist_sq > 0:
            cx = tx * TILE_SIZE + TILE_SIZE // 2
            cy = ty * TILE_SIZE + TILE_SIZE // 2
            if _too_close_px(cx, cy, min_px_dist_sq, npc_tiles):
                continue
            if _too_close_px(cx, cy, min_px_dist_sq, reserved_tiles.union(reserved_global)):
                continue
        return (tx, ty)

    return None
