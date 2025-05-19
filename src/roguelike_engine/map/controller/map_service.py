# Path: src/roguelike_engine/map/controller/map_service.py
import logging
import random
from typing import Optional, List, Tuple, Dict

from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.map.model.zone import Zone
from roguelike_engine.map.model.generator.factory import get_generator
from roguelike_engine.map.model.loader.factory import get_map_loader
from roguelike_engine.map.model.map_model import Map
from roguelike_engine.map.utils import (
    generate_lobby_matrix,
    calculate_lobby_offset,
    calculate_dungeon_offset,
    find_lobby_exit
)
from roguelike_engine.map.model.generator.dungeon import DungeonGenerator

logger = logging.getLogger(__name__)

class MapService:
    """
    Servicio para generación, carga y fusión de mapas utilizando zonas:
    'lobby', 'dungeon' y un 'world' que agrupa todo.
    """
    def __init__(
        self,
        generator_name: str = "dungeon",
        loader_name: str = "text",
        exporter=None,
    ):
        self.generator = get_generator(generator_name)
        self.loader = get_map_loader(loader_name)
        self.exporter = exporter

    def build_map(self, map_name: Optional[str] = None) -> Map:
        key = map_name or "global_map"

        # 1) Crear zona 'world' que representa todo el mapa global
        world = Zone(key, (0, 0),
                     global_map_settings.global_width,
                     global_map_settings.global_height)

        # 2) Generar y colocar el lobby
        lobby_offset = self._place_lobby_zone(world)

        # 3) Generar y colocar la dungeon
        dungeon_info = self._place_dungeon_zone(world, lobby_offset)

        # 4) Serializar matriz global a filas de texto
        rows = ["".join(row) for row in world.matrix]

        # 5) Cargar tiles y overlay para el mapa completo
        _, tiles, overlay = self.loader.load(rows, key)

        metadata = dungeon_info["metadata"]
        metadata["lobby_offset"] = lobby_offset

        return Map(rows, tiles, overlay, metadata, key)

    def _place_lobby_zone(self, world: Zone) -> Tuple[int, int]:
        rows = generate_lobby_matrix()
        offset = calculate_lobby_offset()
        lobby = Zone("lobby", offset)
        lobby.set_matrix_from_rows(rows)
        self._merge_zone_into_world(world, lobby)
        return offset

    def _place_dungeon_zone(
        self,
        world: Zone,
        lobby_offset: Tuple[int, int]
    ) -> Dict[str, object]:
        raw_map, metadata = self.generator.generate(
            width=global_map_settings.zone_width,
            height=global_map_settings.zone_height,
            return_rooms=True,
        )
        offset = calculate_dungeon_offset(lobby_offset)
        dungeon = Zone("dungeon", offset)
        dungeon_rows = ["".join(r) for r in raw_map]
        dungeon.set_matrix_from_rows(dungeon_rows)
        self._merge_zone_into_world(world, dungeon)
        # Conectar túneles
        self._connect_tunnels_in_world(
            world,
            lobby_offset,
            dungeon,
            metadata.get("rooms", [])
        )
        return {"offset": offset, "metadata": metadata}

    def _merge_zone_into_world(self, world: Zone, zone: Zone) -> None:
        for y in range(zone.height):
            for x in range(zone.width):
                world.matrix[zone.offset_y + y][zone.offset_x + x] = zone.matrix[y][x]

    def _connect_tunnels_in_world(
        self,
        world: Zone,
        lobby_offset: Tuple[int, int],
        dungeon: Zone,
        rooms: List[Tuple[int, int, int, int]]
    ) -> None:
        # Punto de salida en el lobby (global coords)
        local_exit = find_lobby_exit(
            generate_lobby_matrix(),
            global_map_settings.dungeon_connect_side
        )
        ex = lobby_offset[0] + local_exit[0]
        ey = lobby_offset[1] + local_exit[1]

        # Centros de habitaciones en coords globales
        centers = [
            ((r[0] + r[2]) // 2 + dungeon.offset_x,
             (r[1] + r[3]) // 2 + dungeon.offset_y)
            for r in rooms
        ]
        if not centers:
            return
        bx, by = min(centers, key=lambda c: abs(c[0] - ex) + abs(c[1] - ey))

        # Dibujar túneles en la matriz del world
        if random.random() < 0.5:
            DungeonGenerator._horiz_tunnel(world.matrix, ex, bx, ey)
            DungeonGenerator._vert_tunnel(world.matrix, ey, by, bx)
        else:
            DungeonGenerator._vert_tunnel(world.matrix, ey, by, ex)
            DungeonGenerator._horiz_tunnel(world.matrix, ex, bx, by)
