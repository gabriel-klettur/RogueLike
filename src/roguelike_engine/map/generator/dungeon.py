# Path: src/roguelike_engine/map/generator/dungeon.py

import random
from typing import List, Tuple, Dict, Optional

from .interfaces import MapGenerator
from src.roguelike_engine.config_map import (
    DUNGEON_WIDTH,
    DUNGEON_HEIGHT,
    DUNGEON_TUNNEL_THICKNESS,
    DUNGEON_MAX_ROOMS,
)
from roguelike_engine.map.utils import intersect, center_of

class DungeonGenerator(MapGenerator):
    """
    Generador de dungeon procedural basado en habitaciones y túneles.
    Soporta:
      - grosor de túneles via DUNGEON_TUNNEL_THICKNESS
      - límite de habitaciones via DUNGEON_MAX_ROOMS (None, int o 'MAX')
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
        # Determinar número máximo de habitaciones
        if DUNGEON_MAX_ROOMS == 'MAX':
            max_allowed = (width // room_min_size) * (height // room_min_size)
        elif isinstance(DUNGEON_MAX_ROOMS, int):
            max_allowed = DUNGEON_MAX_ROOMS
        else:
            max_allowed = max_rooms

        print(f"[Dungeon] Generación iniciada. Intentos permitidos: {max_rooms}, límite real de habitaciones: {max_allowed}")

        map_ = [["#" for _ in range(width)] for _ in range(height)]
        rooms: List[Tuple[int,int,int,int]] = []
        attempts = 0

        while attempts < max_rooms and len(rooms) < max_allowed:
            attempts += 1
            w = random.randint(room_min_size, room_max_size)
            h = random.randint(room_min_size, room_max_size)
            x = random.randint(1, width - w - 1)
            y = random.randint(1, height - h - 1)
            new_room = (x, y, x + w, y + h)

            if any(intersect(r, new_room) for r in rooms):
                print(f"  ▸ Intento {attempts}: habitación colisiona, descartada.")
                continue

            if avoid_zone and not (
                new_room[2] < avoid_zone[0] or
                new_room[0] > avoid_zone[2] or
                new_room[3] < avoid_zone[1] or
                new_room[1] > avoid_zone[3]
            ):
                print(f"  ▸ Intento {attempts}: dentro de zona evitada, descartada.")
                continue

            # Pintar habitación
            for yy in range(y, y + h):
                for xx in range(x, x + w):
                    map_[yy][xx] = "O"

            print(f"  ✅ Intento {attempts}: habitación creada en {(x,y)} tamaño {(w,h)} (Total habitaciones: {len(rooms)+1}).")

            # Conectar con la anterior si existe
            if rooms:
                prev_center = center_of(rooms[-1])
                new_center  = center_of(new_room)
                if random.random() < 0.5:
                    self._horiz_tunnel(map_, prev_center[0], new_center[0], prev_center[1])
                    self._vert_tunnel (map_, prev_center[1], new_center[1], new_center[0])
                else:
                    self._vert_tunnel (map_, prev_center[1], new_center[1], prev_center[0])
                    self._horiz_tunnel(map_, prev_center[0], new_center[0], new_center[1])

            rooms.append(new_room)

        print(f"[Dungeon] Generación finalizada: {len(rooms)} habitaciones creadas en {attempts} intentos.\n")

        metadata = {"rooms": rooms}
        return map_, metadata

    @staticmethod
    def _horiz_tunnel(map_: List[List[str]], x1: int, x2: int, y: int) -> None:
        half = DUNGEON_TUNNEL_THICKNESS // 2
        for x in range(min(x1, x2), max(x1, x2) + 1):
            for t in range(DUNGEON_TUNNEL_THICKNESS):
                yy = y + t - half
                if 0 <= yy < len(map_):
                    map_[yy][x] = "="

    @staticmethod
    def _vert_tunnel(map_: List[List[str]], y1: int, y2: int, x: int) -> None:
        half = DUNGEON_TUNNEL_THICKNESS // 2
        for yy in range(min(y1, y2), max(y1, y2) + 1):
            for t in range(DUNGEON_TUNNEL_THICKNESS):
                xx = x + t - half
                if 0 <= xx < len(map_[0]):
                    map_[yy][xx] = "="
