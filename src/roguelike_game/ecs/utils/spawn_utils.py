"""spawn_utils.py: Utilities for entity spawning .
"""

# Path: src/roguelike_game/ecs/utils/spawn_utils.py
import random
from roguelike_engine.config.config_tiles import TILE_SIZE

def find_spawn_positions(map_manager, buildings, lobby_offset, zone_size, neighbor_padding=1, sample_count=100):
    """Devuelve hasta sample_count posiciones de spawn válidas muestreadas aleatoriamente."""
    lx, ly = lobby_offset
    w, h = zone_size
    # Coords bloqueadas por terreno y edificios
    solid_coords = {(t.rect.x//TILE_SIZE, t.rect.y//TILE_SIZE) for t in map_manager.solid_tiles}
    building_coords = {(r.x//TILE_SIZE, r.y//TILE_SIZE) for b in buildings for r in b.collision_tiles}
    positions = []
    attempts = 0
    max_attempts = sample_count * 10
    while len(positions) < sample_count and attempts < max_attempts:
        x = random.randrange(lx, lx + w)
        y = random.randrange(ly, ly + h)
        attempts += 1
        if (x, y) in positions or (x, y) in solid_coords:
            continue
        # Verificar vecinos de terreno
        blocked = False
        for dx in range(-neighbor_padding, neighbor_padding + 1):
            for dy in range(-neighbor_padding, neighbor_padding + 1):
                if dx == 0 and dy == 0:
                    continue
                if (x + dx, y + dy) in solid_coords:
                    blocked = True
                    break
            if blocked:
                break
        if blocked or (x, y) in building_coords:
            continue
        # Verificar vecinos de edificios
        for dx in range(-neighbor_padding, neighbor_padding + 1):
            for dy in range(-neighbor_padding, neighbor_padding + 1):
                if dx == 0 and dy == 0:
                    continue
                if (x + dx, y + dy) in building_coords:
                    blocked = True
                    break
            if blocked:
                break
        if blocked:
            continue
        positions.append((x, y))
    return positions