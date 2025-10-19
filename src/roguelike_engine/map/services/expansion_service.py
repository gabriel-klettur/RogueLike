import random
import logging

from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.config.config_tiles import TILE_SIZE

logger = logging.getLogger(__name__)

# ─── Helpers ─────────────────────────────────────────────────────────────

def _next_zone_key(base: str = 'extra_dungeon') -> tuple[str, str]:
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


def _player_tile_and_subtile(world) -> tuple[int, int, int, int, float, float, str | None]:
    """
    Convierte la posición del jugador a coordenadas de tile y subpixel.
    Retorna (tx, ty, rel_x, rel_y, sub_x, sub_y, current_zone).
    """
    pos = getattr(world, 'player_position', None)
    if pos is None:
        # Fallback defensivo: usar centro del lobby
        lob_x, lob_y = world.map_manager.lobby_offset
        px = (lob_x + global_map_settings.zone_width // 2) * TILE_SIZE
        py = (lob_y + global_map_settings.zone_height // 2) * TILE_SIZE
    else:
        px, py = pos.x, pos.y
    tx, ty = int(px) // TILE_SIZE, int(py) // TILE_SIZE
    # determina zona actual
    current_zone: str | None = None
    offsets = global_map_settings.zone_offsets
    for zone, (ox, oy) in offsets.items():
        if ox <= tx < ox + global_map_settings.zone_width and oy <= ty < oy + global_map_settings.zone_height:
            current_zone = zone
            break
    # coordenadas relativas (tiles)
    ox, oy = offsets.get(current_zone, (0, 0))
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

    def _resolve_zone_offset(zkey: str) -> tuple[int, int]:
        if zkey in global_map_settings.zone_offsets:
            return global_map_settings.zone_offsets[zkey]
        seen: set[str] = set()
        cur = zkey
        off = global_map_settings.zone_offsets.get('dungeon', (0, 0))
        while cur in getattr(global_map_settings, 'additional_zones', {}) and cur not in seen:
            seen.add(cur)
            parent, side = global_map_settings.additional_zones[cur]
            base = global_map_settings.zone_offsets[parent] if parent in global_map_settings.zone_offsets else _resolve_zone_offset(parent)
            off = global_map_settings.calculate_offset(base, side)
            cur = parent
        return off

    used = set(global_map_settings.zone_offsets.values())
    for k in getattr(global_map_settings, 'additional_zones', {}).keys():
        try:
            used.add(_resolve_zone_offset(k))
        except Exception:
            pass

    parent_off = _resolve_zone_offset(parent_key)
    valid = [s for s in all_sides if global_map_settings.calculate_offset(parent_off, s) not in used]
    return random.choice(valid) if valid else 'bottom'


# ─── Servicio principal ──────────────────────────────────────────────────

def expand_dungeon(world) -> tuple[str, str]:
    """
    Añade una nueva zona conectada y ajusta al jugador. No depende de eventos.
    Retorna (new_key, parent_key).
    """
    map_manager = world.map_manager
    # 1) Determinar key y padre
    new_key, parent_key = _next_zone_key()

    # 2) Guardar estado jugador (en coordenadas relativas y subpixels)
    _tx, _ty, rel_x, rel_y, sub_x, sub_y, current_zone = _player_tile_and_subtile(world)

    # 3) Registrar y expandir
    side = _choose_side(parent_key)
    global_map_settings.additional_zones[new_key] = (parent_key, side)
    map_manager.expand_zone(side, new_key, parent_key)

    # 4) Reposicionar jugador manteniendo su posición relativa en su zona
    if current_zone and getattr(world, 'player_entity', None) is not None:
        off_x, off_y = global_map_settings.zone_offsets[current_zone]
        new_tx = off_x + rel_x
        new_ty = off_y + rel_y
        pos_map = world.components.get('Position', {})
        pos_comp = pos_map.get(world.player_entity)
        if pos_comp is not None:
            pos_comp.x = new_tx * TILE_SIZE + sub_x
            pos_comp.y = new_ty * TILE_SIZE + sub_y

    logger.debug(f"🗺️ Añadida zona '{new_key}' conectada a '{parent_key}' y recargando mapa...")
    return new_key, parent_key
