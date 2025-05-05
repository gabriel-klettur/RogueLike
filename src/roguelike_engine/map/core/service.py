# Path: src/roguelike_engine/map/core/service.py

import logging
import random
from typing import Optional, Tuple, List, Dict

from src.roguelike_engine.config_map import (
    GLOBAL_WIDTH,
    GLOBAL_HEIGHT,
    DUNGEON_WIDTH,
    DUNGEON_HEIGHT,
    LOBBY_WIDTH,
    LOBBY_HEIGHT,
    DUNGEON_CONNECT_SIDE,
)
from roguelike_engine.map.generator.factory import get_generator
from roguelike_engine.map.merger.factory import get_merger
from roguelike_engine.map.loader.factory import get_map_loader
from roguelike_engine.map.exporter.factory import get_exporter, MapExporter
from roguelike_engine.map.core.model import MapData
from roguelike_engine.map.generator.dungeon import DungeonGenerator

logger = logging.getLogger(__name__)


def _generate_lobby_matrix() -> List[str]:
    """
    Genera dinámicamente el mapa del lobby de tamaño LOBBY_WIDTH×LOBBY_HEIGHT:
    - Borde de muros '#'
    - Interior de suelo '.'
    """
    matrix: List[str] = []
    for y in range(LOBBY_HEIGHT):
        if y == 0 or y == LOBBY_HEIGHT - 1:
            matrix.append("#" * LOBBY_WIDTH)
        else:
            matrix.append("#" + "." * (LOBBY_WIDTH - 2) + "#")
    return matrix


def _find_lobby_exit(lobby: List[str], side: str) -> Tuple[int, int]:
    """
    Encuentra un punto de salida en el lobby según el lado indicado:
      - 'bottom', 'top', 'left', 'right'
    """
    h = len(lobby)
    w = len(lobby[0])
    if side == "bottom":
        for x in range(w):
            if lobby[h-1][x] == '.':
                return x, h-1
        return w//2, h-1
    if side == "top":
        for x in range(w):
            if lobby[0][x] == '.':
                return x, 0
        return w//2, 0
    if side == "left":
        for y in range(h):
            if lobby[y][0] == '.':
                return 0, y
        return 0, h//2
    # 'right'
    for y in range(h):
        if lobby[y][w-1] == '.':
            return w-1, y
    return w-1, h//2


def _calculate_lobby_offset() -> Tuple[int, int]:
    """
    Determina el offset (x,y) para centrar el lobby en la celda central de un grid.
    """
    n_cols = GLOBAL_WIDTH // LOBBY_WIDTH
    n_rows = GLOBAL_HEIGHT // LOBBY_HEIGHT
    if n_cols < 1 or n_rows < 1:
        return ((GLOBAL_WIDTH - LOBBY_WIDTH)//2,
                (GLOBAL_HEIGHT - LOBBY_HEIGHT)//2)
    center_col = n_cols // 2
    center_row = n_rows // 2
    rem_x = GLOBAL_WIDTH - n_cols * LOBBY_WIDTH
    rem_y = GLOBAL_HEIGHT - n_rows * LOBBY_HEIGHT
    start_x = rem_x // 2
    start_y = rem_y // 2
    return (start_x + center_col * LOBBY_WIDTH,
            start_y + center_row * LOBBY_HEIGHT)


def _calculate_dungeon_offset(lobby_off: Tuple[int,int], side: str) -> Tuple[int,int]:
    """
    Calcula el offset (x,y) para colocar la dungeon adyacente al lobby
    según DUNGEON_CONNECT_SIDE: arriba, abajo, izquierda, derecha.
    """
    off_x, off_y = lobby_off
    if side == "bottom":
        return off_x, off_y + LOBBY_HEIGHT
    if side == "top":
        return off_x, off_y - DUNGEON_HEIGHT
    if side == "left":
        return off_x - DUNGEON_WIDTH, off_y
    # 'right'
    return off_x + LOBBY_WIDTH, off_y


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
        *,
        width: int = DUNGEON_WIDTH,
        height: int = DUNGEON_HEIGHT,
        offset_x: int = 0,
        offset_y: int = 0,
        map_mode: str = "combined",
        map_name: Optional[str] = None,
        export_debug: bool = True,
    ) -> MapData:

        if map_mode == "global":
            key = map_name or "global_map"

            # canvas global inicial
            canvas = [["#" for _ in range(GLOBAL_WIDTH)] for __ in range(GLOBAL_HEIGHT)]

            # 1️⃣ lobby dinámico
            lobby = _generate_lobby_matrix()
            lobby_off = _calculate_lobby_offset()
            lx, ly = lobby_off
            for y, row in enumerate(lobby):
                for x, ch in enumerate(row):
                    canvas[ly+y][lx+x] = ch

            # 2️⃣ dungeon procedural
            raw_map, metadata = self.generator.generate(
                width=DUNGEON_WIDTH,
                height=DUNGEON_HEIGHT,
                return_rooms=True,
            )
            dx, dy = _calculate_dungeon_offset(lobby_off, DUNGEON_CONNECT_SIDE)
            for y, row in enumerate(raw_map):
                for x, ch in enumerate(row):
                    gx = dx + x
                    gy = dy + y
                    if 0 <= gx < GLOBAL_WIDTH and 0 <= gy < GLOBAL_HEIGHT:
                        canvas[gy][gx] = ch

            # 3️⃣ conectar pasillos
            exit_local = _find_lobby_exit(lobby, DUNGEON_CONNECT_SIDE)
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
            return MapData(merged, tiles, overlay, metadata, key)

        # modos 'lobby', 'dungeon', 'combined' sin cambios:
        key = map_name or ("lobby_map" if map_mode=="lobby" else "combined_map")
        if map_mode=="lobby":
            merged = _generate_lobby_matrix()
            metadata={}
        else:
            raw_map, metadata = self.generator.generate(
                width=width, height=height,
                return_rooms=(map_mode=="combined"),
                avoid_zone=None
            )
            if map_mode=="dungeon":
                merged=["".join(r) for r in raw_map]
            else:
                lobby=_generate_lobby_matrix()
                fused=self.merger.merge(
                    handmade_map=[list(r) for r in lobby],
                    generated_map=raw_map,
                    offset_x=offset_x,
                    offset_y=offset_y,
                    dungeon_rooms=metadata.get("rooms",[]),
                )
                merged=["".join(r) for r in fused]
                if export_debug:
                    try:
                        fn=self.exporter.export(merged)
                        logger.debug("Mapa debug exportado: %s",fn)
                    except Exception as e:
                        logger.warning("Error export debug: %s",e)
        _, tiles, overlay=self.loader.load(merged, key)
        return MapData(merged, tiles, overlay, metadata, key)
