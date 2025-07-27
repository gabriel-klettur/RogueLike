"""
Utilities for map operations extracted from MapManager.
"""
from roguelike_engine.config.map_config import global_map_settings


def flatten_tiles(tiles_matrix: list[list]) -> list:
    """
    Devuelve todas las tiles del mapa en una lista plana.
    """
    return [tile for row in tiles_matrix for tile in row]


def get_zone_offset(zone_name: str) -> tuple[int, int]:
    """
    Devuelve offsets (col, row) para la zona dada.
    """
    return global_map_settings.zone_offsets.get(zone_name, (0, 0))


def get_zone_at(tiles_matrix: list[list], row: int, col: int) -> tuple[str, int, int]:
    """
    Devuelve nombre de zona y offsets (col, row) para coordenadas globales.
    """
    zone = tiles_matrix[row][col].zone
    offx, offy = get_zone_offset(zone)
    return zone, offx, offy
