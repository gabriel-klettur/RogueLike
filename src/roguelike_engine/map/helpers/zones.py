from typing import Tuple
from roguelike_engine.config.map_config import global_map_settings


def get_zone_for_tile(tile_x: int, tile_y: int) -> str:
    """
    Devuelve el nombre de zona en la que cae la tile (tile_x,tile_y),
    comparando contra zone_offsets y zone_size de `global_map_settings`.
    Retorna 'no zone' cuando no cae dentro de ninguna zona.
    """
    w, h = global_map_settings.zone_size
    for zone, (ox, oy) in global_map_settings.zone_offsets.items():
        if ox <= tile_x < ox + w and oy <= tile_y < oy + h:
            return zone
    return "no zone"
