# src/roguelike_engine/map/generator/dungeon.py
import random
from typing import List, Tuple, Dict, Optional
from .interfaces import MapGenerator
from src.roguelike_engine.config_map import DUNGEON_WIDTH, DUNGEON_HEIGHT
from roguelike_engine.map.utils import intersect, center_of

class DungeonGenerator(MapGenerator):
    """
    Generador de dungeon procedural basado en habitaciones y túneles.
    """
    def generate(
        self,
        width: int = DUNGEON_WIDTH,
        height: int = DUNGEON_HEIGHT,
        max_rooms: int = 10,
        room_min_size: int = 10,
        room_max_size: int = 20,
        avoid_zone: Optional[Tuple[int,int,int,int]] = None,
        **kwargs
    ) -> Tuple[List[List[str]], Dict]:
        map_ = [["#" for _ in range(width)] for _ in range(height)]
        rooms: List[Tuple[int,int,int,int]] = []

        for idx in range(max_rooms):
            w = random.randint(room_min_size, room_max_size)
            h = random.randint(room_min_size, room_max_size)
            x = random.randint(1, width - w - 1)
            y = random.randint(1, height - h - 1)
            new_room = (x, y, x + w, y + h)

            # Colisión con otras habitaciones
            if any(intersect(r, new_room) for r in rooms):
                continue

            # Zona protegida
            if avoid_zone and not (
                new_room[2] < avoid_zone[0] or
                new_room[0] > avoid_zone[2] or
                new_room[3] < avoid_zone[1] or
                new_room[1] > avoid_zone[3]
            ):
                continue

            # Pintar habitación
            for yy in range(y, y + h):
                for xx in range(x, x + w):
                    map_[yy][xx] = "O"

            # Conectar con la anterior
            if rooms:
                prev_center = center_of(rooms[-1])
                new_center = center_of(new_room)
                if random.random() < 0.5:
                    self._horiz_tunnel(map_, prev_center[0], new_center[0], prev_center[1])
                    self._vert_tunnel(map_, prev_center[1], new_center[1], new_center[0])
                else:
                    self._vert_tunnel(map_, prev_center[1], new_center[1], prev_center[0])
                    self._horiz_tunnel(map_, prev_center[0], new_center[0], new_center[1])

            rooms.append(new_room)

        metadata = {"rooms": rooms}
        return map_, metadata

    @staticmethod
    def _horiz_tunnel(map_: List[List[str]], x1: int, x2: int, y: int) -> None:
        for x in range(min(x1, x2), max(x1, x2) + 1):
            map_[y][x] = "="

    @staticmethod
    def _vert_tunnel(map_: List[List[str]], y1: int, y2: int, x: int) -> None:
        for y in range(min(y1, y2), max(y1, y2) + 1):
            map_[y][x] = "="