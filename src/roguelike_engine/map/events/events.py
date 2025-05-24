# Path: src/roguelike_engine/map/events/events.py
import random
import pygame

from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.config.config_tiles import TILE_SIZE

# ─── Helpers ─────────────────────────────────────────────────────────────

def _next_zone_key(base='extra_dungeon') -> tuple[str, str]:
    """
    Determina la nueva clave de zona y su zona padre.
    Retorna (new_key, parent_key).
    """
    max_idx = 0
    for key in global_map_settings.additional_zones:
        if key == base:
            idx = 1
        elif key.startswith(base) and key[len(base):].isdigit():
            idx = int(key[len(base):])
        else:
            continue
        max_idx = max(max_idx, idx)
    new_idx = max_idx + 1

    if new_idx == 1:
        return base, 'dungeon'
    parent = base if new_idx == 2 else f"{base}{new_idx-1}"
    return f"{base}{new_idx}", parent


def _player_tile_and_subtile(entities) -> tuple[int,int,int,int,int]:
    """
    Convierte la posición del jugador a coordenadas de tile y subpixel.
    Retorna (tx, ty, rel_x, rel_y, sub_x, sub_y, current_zone).
    """
    px, py = entities.player.x, entities.player.y
    tx, ty = int(px) // TILE_SIZE, int(py) // TILE_SIZE
    # determina zona actual
    current_zone = None
    offsets = global_map_settings.zone_offsets
    for zone, (ox, oy) in offsets.items():
        if ox <= tx < ox + global_map_settings.zone_width and oy <= ty < oy + global_map_settings.zone_height:
            current_zone = zone
            break
    # coordenadas relativas (tiles)
    ox, oy = offsets.get(current_zone, (0,0))
    rel_x, rel_y = tx - ox, ty - oy
    # subpixel
    sub_x = px - tx * TILE_SIZE
    sub_y = py - ty * TILE_SIZE
    return tx, ty, rel_x, rel_y, sub_x, sub_y, current_zone


def _choose_side(parent_key: str) -> str:
    """
    Selecciona un lado válido para expandir sin solapar zonas existentes.
    """
    all_sides = ['bottom', 'top', 'left', 'right']
    used = set(global_map_settings.zone_offsets.values())
    parent_off = global_map_settings.zone_offsets[parent_key]
    valid = [s for s in all_sides
             if global_map_settings.calculate_offset(parent_off, s) not in used]
    return random.choice(valid) if valid else 'bottom'


# ─── Main handler ─────────────────────────────────────────────────────────

def handle_expand_dungeon(event, map_manager, entities) -> bool:
    """
    Gestiona F3: añade una nueva zona conectada y ajusta al jugador.
    """
    if event.type != pygame.KEYDOWN or event.key != pygame.K_F3:
        return False

    # 1) Determinar key y padre
    new_key, parent_key = _next_zone_key()
    
    # 2) Guardar estado jugador
    tx, ty, rel_x, rel_y, sub_x, sub_y, current_zone = _player_tile_and_subtile(entities)

    # 3) Registrar y expandir
    side = _choose_side(parent_key)
    global_map_settings.additional_zones[new_key] = (parent_key, side)
    map_manager.expand_zone(side, new_key, parent_key)

    # 4) Reposicionar jugador
    if current_zone:
        off_x, off_y = global_map_settings.zone_offsets[current_zone]
        new_tx = off_x + rel_x
        new_ty = off_y + rel_y
        entities.player.x = new_tx * TILE_SIZE + sub_x
        entities.player.y = new_ty * TILE_SIZE + sub_y

    print(f"🗺️ Añadida zona '{new_key}' conectada a '{parent_key}' y recargando mapa...")
    return True
