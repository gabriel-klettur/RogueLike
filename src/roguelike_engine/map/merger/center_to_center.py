#Path: src/roguelike_engine/map/merger/center_to_center.py

import random
from typing import List, Tuple, Sequence, Optional
import logging
from .interfaces import MergerStrategy
from roguelike_engine.map.utils import find_closest_room_center
from roguelike_engine.map.generator.dungeon import DungeonGenerator

logger = logging.getLogger(__name__)

class CenterToCenterMerger(MergerStrategy):
    """
    Conecta el centro del lobby con el centro de la sala más cercana.
    """
    def merge(
        self,
        handmade_map: Sequence[Sequence[str]],
        generated_map: Sequence[Sequence[str]],
        offset_x: int = 0,
        offset_y: int = 0,
        dungeon_rooms: Optional[List[Tuple[int,int,int,int]]] = None
    ) -> List[List[str]]:
        # Superponer
        new_map = [list(row) for row in generated_map]
        h, w = len(new_map), len(new_map[0]) if new_map else 0
        for y, row in enumerate(handmade_map):
            for x, ch in enumerate(row):
                tx, ty = x + offset_x, y + offset_y
                if 0 <= ty < h and 0 <= tx < w:
                    new_map[ty][tx] = ch

        # Conexión
        if dungeon_rooms:
            # Buscar salida en lobby
            exit_pos = _find_exit(handmade_map, offset_x, offset_y)
            if not exit_pos:
                logger.warning("No exit found; using center bottom.")
                mid_x = len(handmade_map[0]) // 2
                _force_exit(handmade_map, mid_x, len(handmade_map)-1)
                exit_pos = (offset_x + mid_x, offset_y + len(handmade_map) -1)

            ex, ey = exit_pos
            cx, cy = find_closest_room_center(ex, ey, dungeon_rooms)
            # Túneles
            if random.random() < 0.5:
                DungeonGenerator._horiz_tunnel(new_map, ex, cx, ey)
                DungeonGenerator._vert_tunnel(new_map, ey, cy, cx)
            else:
                DungeonGenerator._vert_tunnel(new_map, ey, cy, ex)
                DungeonGenerator._horiz_tunnel(new_map, ex, cx, cy)
        return new_map


def _find_exit(
    lobby: Sequence[Sequence[str]],
    ox: int,
    oy: int
) -> Optional[Tuple[int,int]]:
    h = len(lobby)
    w = len(lobby[0]) if h else 0
    # Inferidor de salida (abajo y lados)
    for x in range(w):
        if lobby[h-1][x] == '.':
            return (ox+x, oy+h-1)
    for y in range(h):
        if lobby[y][0] == '.': return (ox, oy+y)
        if lobby[y][w-1] == '.': return (ox+w-1, oy+y)
    return None


def _force_exit(
    lobby: Sequence[Sequence[str]],
    x: int,
    y: int
) -> None:
    row = list(lobby[y])
    row[x] = '.'
    try:
        lobby[y] = type(lobby[y])(''.join(row))
    except Exception:
        pass
    """
    Conecta el centro del lobby con el centro de la sala más cercana.
    """
    def merge(
        self,
        handmade_map: Sequence[Sequence[str]],
        generated_map: Sequence[Sequence[str]],
        offset_x: int = 0,
        offset_y: int = 0,
        dungeon_rooms: Optional[List[Tuple[int,int,int,int]]] = None
    ) -> List[List[str]]:
        # Superponer
        new_map = [list(row) for row in generated_map]
        h, w = len(new_map), len(new_map[0]) if new_map else 0
        for y, row in enumerate(handmade_map):
            for x, ch in enumerate(row):
                tx, ty = x + offset_x, y + offset_y
                if 0 <= ty < h and 0 <= tx < w:
                    new_map[ty][tx] = ch

        # Conexión
        if dungeon_rooms:
            # Buscar salida en lobby
            exit_pos = _find_exit(handmade_map, offset_x, offset_y)
            if not exit_pos:
                logger.warning("No exit found; using center bottom.")
                mid_x = len(handmade_map[0]) // 2
                _force_exit(handmade_map, mid_x, len(handmade_map)-1)
                exit_pos = (offset_x + mid_x, offset_y + len(handmade_map) -1)

            ex, ey = exit_pos
            cx, cy = find_closest_room_center(ex, ey, dungeon_rooms)
            # Túneles
            if random.random() < 0.5:
                DungeonGenerator._horiz_tunnel(new_map, ex, cx, ey)
                DungeonGenerator._vert_tunnel(new_map, ey, cy, cx)
            else:
                DungeonGenerator._vert_tunnel(new_map, ey, cy, ex)
                DungeonGenerator._horiz_tunnel(new_map, ex, cx, cy)
        return new_map


    def _find_exit(
        lobby: Sequence[Sequence[str]],
        ox: int,
        oy: int
    ) -> Optional[Tuple[int,int]]:
        h = len(lobby)
        w = len(lobby[0]) if h else 0
        # Inferidor de salida (abajo y lados)
        for x in range(w):
            if lobby[h-1][x] == '.':
                return (ox+x, oy+h-1)
        for y in range(h):
            if lobby[y][0] == '.': return (ox, oy+y)
            if lobby[y][w-1] == '.': return (ox+w-1, oy+y)
        return None


    def _force_exit(
        lobby: Sequence[Sequence[str]],
        x: int,
        y: int
    ) -> None:
        row = list(lobby[y])
        row[x] = '.'
        try:
            lobby[y] = type(lobby[y])(''.join(row))
        except Exception:
            pass