"""spawn_utils.py: Utilities for entity spawning .
"""

import random
from roguelike_engine.config.config_tiles import TILE_SIZE

def find_spawn_positions(map_manager, buildings, lobby_offset, zone_size, neighbor_padding=1, sample_count=100):
    """Devuelve una muestra de posiciones de spawn válidas dentro del lobby."""
    lx, ly = lobby_offset
    w, h = zone_size
    # Tiles bloqueados por terreno
    solid_coords = {(t.rect.x//TILE_SIZE, t.rect.y//TILE_SIZE) for t in map_manager.solid_tiles}
    # Tiles bloqueados por edificios
    building_coords = {(r.x//TILE_SIZE, r.y//TILE_SIZE) for b in buildings for r in b.collision_tiles}
    # Candidatos en el lobby
    candidates = [(x, y) for x in range(lx, lx + w) for y in range(ly, ly + h)]
    # Filtrar terreno sólido y vecinos libres
    valid = [c for c in candidates if c not in solid_coords and all(
        ((c[0]+dx, c[1]+dy) not in solid_coords) for dx in range(-neighbor_padding, neighbor_padding+1) for dy in range(-neighbor_padding, neighbor_padding+1) if (dx,dy) != (0,0)
    )]
    # Filtrar colisiones con edificios y vecinos libres
    free = [c for c in valid if c not in building_coords and all(
        ((c[0]+dx, c[1]+dy) not in building_coords) for dx in range(-neighbor_padding, neighbor_padding+1) for dy in range(-neighbor_padding, neighbor_padding+1) if (dx,dy) != (0,0)
    )]
    # Muestra final
    count = min(sample_count, len(free))
    return random.sample(free, count)