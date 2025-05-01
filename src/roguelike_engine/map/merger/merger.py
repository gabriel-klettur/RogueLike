# Path: src/roguelike_engine/map/merger/merger.py

import random
import logging
from typing import List, Tuple, Optional, Sequence

from roguelike_engine.map.utils import find_closest_room_center
# Importamos túneles desde el generador si fuera necesario
from roguelike_engine.map.generator.dungeon import DungeonGenerator
from roguelike_engine.map.generator.interfaces import MapGenerator

logger = logging.getLogger(__name__)


def merge_handmade_with_generated(
    handmade_map: Sequence[Sequence[str]],
    generated_map: Sequence[Sequence[str]],
    offset_x: int = 0,
    offset_y: int = 0,
    merge_mode: str = "center_to_center",
    dungeon_rooms: Optional[List[Tuple[int, int, int, int]]] = None,
) -> List[str]:
    """
    Fusiona un mapa "handmade" con uno "procedural".

    :param handmade_map: matriz del mapa manual.
    :param generated_map: matriz del mapa generado.
    :param offset_x: desplazamiento X para el handmade.
    :param offset_y: desplazamiento Y para el handmade.
    :param merge_mode: estrategia de conexión ('center_to_center').
    :param dungeon_rooms: lista de habitaciones (para conectar).
    :returns: lista de filas como strings.
    """
    logger.debug("Iniciando fusión del lobby con dungeon (modo=%s)...", merge_mode)
    # Clonamos mapa generado como lista de listas
    new_map: List[List[str]] = [list(row) for row in generated_map]

    # Superponer handmade_map en new_map
    height = len(new_map)
    width = len(new_map[0]) if height else 0

    for y, row in enumerate(handmade_map):
        for x, char in enumerate(row):
            tx = x + offset_x
            ty = y + offset_y
            if 0 <= ty < height and 0 <= tx < width:
                new_map[ty][tx] = char

    # Conexión automática
    if merge_mode == "center_to_center" and dungeon_rooms:
        logger.debug("Aplicando conexión center_to_center...")
        _connect_center_to_center(new_map, handmade_map, offset_x, offset_y, dungeon_rooms)
    else:
        logger.debug("No se aplicó conexión automática (modo=%s o sin habitaciones).", merge_mode)

    # Convertir a lista de strings
    return ["".join(row) for row in new_map]


def _connect_center_to_center(
    map_grid: List[List[str]],
    handmade_map: Sequence[Sequence[str]],
    offset_x: int,
    offset_y: int,
    dungeon_rooms: List[Tuple[int, int, int, int]],
) -> None:
    # Buscamos salida del lobby
    exit_pos = _find_exit_from_lobby(handmade_map, offset_x, offset_y)
    if not exit_pos:
        logger.warning("No se encontró una salida válida en el lobby; forzando salida central.")
        mid_x = len(handmade_map[0]) // 2
        _ensure_lobby_exit_at(handmade_map, mid_x, len(handmade_map) - 1)
        exit_pos = (offset_x + mid_x, offset_y + len(handmade_map) - 1)

    exit_x, exit_y = exit_pos
    logger.debug("Punto de salida del lobby: (%d, %d)", exit_x, exit_y)

    # Sala destino más cercana
    target_x, target_y = find_closest_room_center(exit_x, exit_y, dungeon_rooms)
    logger.debug("Sala destino más cercana: (%d, %d)", target_x, target_y)

    # Elegir trayectoria
    if random.random() < 0.5:
        logger.debug("Conexión: horizontal➝vertical")
        DungeonGenerator._horiz_tunnel(map_grid, exit_x, target_x, exit_y)
        DungeonGenerator._vert_tunnel(map_grid, exit_y, target_y, target_x)
    else:
        logger.debug("Conexión: vertical➝horizontal")
        DungeonGenerator._vert_tunnel(map_grid, exit_y, target_y, exit_x)
        DungeonGenerator._horiz_tunnel(map_grid, exit_x, target_x, target_y)


def _find_exit_from_lobby(
    lobby_map: Sequence[Sequence[str]],
    offset_x: int,
    offset_y: int,
) -> Optional[Tuple[int, int]]:
    height = len(lobby_map)
    width = len(lobby_map[0]) if height else 0

    # Borde inferior
    for x in range(width):
        if lobby_map[height - 1][x] == ".":
            return (offset_x + x, offset_y + height - 1)

    # Bordes laterales
    for y in range(height):
        if lobby_map[y][0] == ".":
            return (offset_x, offset_y + y)
        if lobby_map[y][width - 1] == ".":
            return (offset_x + width - 1, offset_y + y)

    return None


def _ensure_lobby_exit_at(
    lobby_map: Sequence[Sequence[str]],
    x: int,
    y: int,
) -> None:
    # Forzamos '.' en la salida
    row = list(lobby_map[y])
    row[x] = "."
    # La estructura lobby_map puede ser de tuplas; si es lista mutamos en sitio
    try:
        if isinstance(lobby_map, list):
            lobby_map[y] = type(lobby_map[y])("".join(row))
    except Exception:
        pass
