
# Path: src/roguelike_engine/map/controller/map_service.py
import logging
import random
from typing import Optional

from roguelike_engine.config_map import (
    GLOBAL_WIDTH,
    GLOBAL_HEIGHT,
    ZONE_WIDTH,
    ZONE_HEIGHT,    
    DUNGEON_CONNECT_SIDE
)
from roguelike_engine.map.model.generator.factory import get_generator
from roguelike_engine.map.model.merger.factory import get_merger
from roguelike_engine.map.model.loader.factory import get_map_loader
from roguelike_engine.map.model.exporter.factory import get_exporter, MapExporter
from roguelike_engine.map.model.map_model import Map
from roguelike_engine.map.model.generator.dungeon import DungeonGenerator
from roguelike_engine.map.utils import (
    generate_lobby_matrix,
    calculate_lobby_offset,
    calculate_dungeon_offset,
    find_lobby_exit
)


logger = logging.getLogger(__name__)
class MapService:
    """
    Genera y carga mapas en modos: 'lobby', 'dungeon', 'combined', 'global'.
    """

    def __init__(
        self,
        generator_name: str = "dungeon",
        merger_name: str = "center_to_center",
        loader_name: str = "text",
        exporter: Optional[MapExporter] = None,
    ):
        self.generator = get_generator(generator_name)
        self.merger    = get_merger(merger_name)
        self.loader    = get_map_loader(loader_name)
        self.exporter: MapExporter = exporter or get_exporter("debug_txt")

    def build_map(
        self,        
        map_name: Optional[str] = None        
    ) -> Map:
        
        key = map_name or "global_map"

        # canvas global inicial
        canvas = [["#" for _ in range(GLOBAL_WIDTH)] for __ in range(GLOBAL_HEIGHT)]

        # 1️⃣ lobby dinámico
        lobby = generate_lobby_matrix()
        lobby_off = calculate_lobby_offset()
        lx, ly = lobby_off
        for y, row in enumerate(lobby):
            for x, ch in enumerate(row):
                canvas[ly+y][lx+x] = ch

        # 2️⃣ dungeon procedural
        raw_map, metadata = self.generator.generate(
            width=ZONE_WIDTH,
            height=ZONE_HEIGHT,
            return_rooms=True,
        )
        dx, dy = calculate_dungeon_offset(lobby_off, DUNGEON_CONNECT_SIDE)
        for y, row in enumerate(raw_map):
            for x, ch in enumerate(row):
                gx = dx + x
                gy = dy + y
                if 0 <= gx < GLOBAL_WIDTH and 0 <= gy < GLOBAL_HEIGHT:
                    canvas[gy][gx] = ch

        # 3️⃣ conectar pasillos
        exit_local = find_lobby_exit(lobby, DUNGEON_CONNECT_SIDE)
        ex, ey = lx + exit_local[0], ly + exit_local[1]
        rooms = metadata.get("rooms", [])
        centers = [(((r[0]+r[2])//2)+dx, ((r[1]+r[3])//2)+dy) for r in rooms]
        if centers:
            bx, by = min(centers, key=lambda c: abs(c[0]-ex)+abs(c[1]-ey))
            if random.random() < 0.5:
                DungeonGenerator._horiz_tunnel(canvas, ex, bx, ey)
                DungeonGenerator._vert_tunnel (canvas, ey, by, bx)
            else:
                DungeonGenerator._vert_tunnel (canvas, ey, by, ex)
                DungeonGenerator._horiz_tunnel(canvas, ex, bx, by)

        # 4️⃣ serializar + cargar
        merged = ["".join(r) for r in canvas]
        _, tiles, overlay = self.loader.load(merged, key)
        metadata["lobby_offset"] = lobby_off
        
        return Map(merged, tiles, overlay, metadata, key)
