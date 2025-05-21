import pygame, time
from collections import deque
from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.config.config_tiles import TILE_SIZE


def handle_expand_dungeon(event, map_manager, entities):
    """
    Maneja F3 para añadir nueva dungeon a la izquierda, recargar mapa y ajustar la posición del jugador.
    Retorna True si procesó el evento.
    """
    if event.type != pygame.KEYDOWN or event.key != pygame.K_F3:
        return False

    base = 'extra_dungeon'
    # determinar índice más alto usado
    max_idx = 0
    for k in global_map_settings.additional_zones:
        if k == base:
            idx = 1
        elif k.startswith(base) and k[len(base):].isdigit():
            idx = int(k[len(base):])
        else:
            continue
        max_idx = max(max_idx, idx)
    new_idx = max_idx + 1

    if new_idx == 1:
        new_key = base
        parent_key = 'lobby'
    else:
        new_key = f"{base}{new_idx}"
        parent_key = base if new_idx == 2 else f"{base}{new_idx-1}"

    # guardar posición del jugador antes de recarga
    px, py = entities.player.x, entities.player.y
    tx, ty = int(px) // TILE_SIZE, int(py) // TILE_SIZE
    old_off = global_map_settings.zone_offsets
    current_zone = None
    for z, (ox, oy) in old_off.items():
        if ox <= tx < ox + global_map_settings.zone_width and oy <= ty < oy + global_map_settings.zone_height:
            current_zone = z
            break
    rel_x = tx - old_off.get(current_zone, (0,0))[0]
    rel_y = ty - old_off.get(current_zone, (0,0))[1]
    sub_x = px - tx * TILE_SIZE
    sub_y = py - ty * TILE_SIZE

    global_map_settings.additional_zones[new_key] = (parent_key, 'left')
    # expansión incremental de la nueva zona
    map_manager.expand_zone('left', new_key, parent_key)

    # ajustar posición del jugador en coords de mundo
    if current_zone:
        new_off = global_map_settings.zone_offsets[current_zone]
        off_x, off_y = new_off
        new_tx = off_x + rel_x
        new_ty = off_y + rel_y
        entities.player.x = new_tx * TILE_SIZE + sub_x
        entities.player.y = new_ty * TILE_SIZE + sub_y

    print(f"🗺️ Añadida zona '{new_key}' conectada a '{parent_key}' y recargando mapa...")
    return True
