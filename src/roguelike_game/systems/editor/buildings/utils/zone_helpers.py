from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_engine.map.utils import get_zone_for_tile
from roguelike_engine.config.map_config import global_map_settings

def assign_zone_and_relatives(building) -> None:
    # 1) Detectar zona basándonos en el centro inferior del sprite
    w_px, h_px = building.image.get_size()
    cx_px = building.x + w_px / 2
    cy_px = building.y + h_px

    tile_x = int(cx_px) // TILE_SIZE
    tile_y = int(cy_px) // TILE_SIZE

    zone = get_zone_for_tile(tile_x, tile_y)

    # 2) Offset de la zona en tiles
    ox, oy = global_map_settings.zone_offsets.get(zone, (0, 0))

    # 3) Convertir ese offset a píxeles
    origin_px_x = ox * TILE_SIZE
    origin_px_y = oy * TILE_SIZE

    # 4) Calcular posición relativa en píxeles
    rel_x = building.x - origin_px_x
    rel_y = building.y - origin_px_y

    # 5) Asignar
    building.zone = zone
    building.rel_x = int(rel_x)
    building.rel_y = int(rel_y)

def detect_zone_from_px(x_px: float, y_px: float) -> tuple[str, tuple[int,int]]:
    """
    Dado un punto en píxeles, devuelve (zone_name, (ox,oy)).
    Si no cae en ninguna zona, devuelve ("no zone", (0,0)).
    """
    tile_x = int(x_px) // TILE_SIZE
    tile_y = int(y_px) // TILE_SIZE
    try:
        zone = get_zone_for_tile(tile_x, tile_y)
    except ValueError:
        zone = "no zone"
    offset = global_map_settings.zone_offsets.get(zone, (0, 0))
    return zone, offset