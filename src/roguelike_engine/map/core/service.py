# Path: src/roguelike_engine/map/core/service.py

import logging
import random
from typing import Optional, Tuple, List, Dict

from src.roguelike_engine.config_map import (
    DUNGEON_WIDTH,
    DUNGEON_HEIGHT,
    GLOBAL_WIDTH,
    GLOBAL_HEIGHT,
    DUNGEON_OFFSET_X,
    DUNGEON_OFFSET_Y,
    LOBBY_OFFSET_X,
    LOBBY_OFFSET_Y,
    LOBBY_WIDTH,
    LOBBY_HEIGHT,
)
from roguelike_engine.map.generator.factory import get_generator
from roguelike_engine.map.merger.factory import get_merger
from roguelike_engine.map.loader.factory import get_map_loader
from roguelike_engine.map.exporter.factory import get_exporter, MapExporter
from roguelike_engine.map.core.model import MapData
from roguelike_engine.map.utils import center_of
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


def _find_lobby_exit(lobby: List[str]) -> Tuple[int, int]:
    """
    Encuentra un punto de salida en el lobby:
    1) Busca '.' en la fila inferior
    2) Si no hay, busca en columnas laterales
    3) Si tampoco, centra en la parte inferior
    Devuelve coordenadas locales (x, y).
    """
    h = len(lobby)
    w = len(lobby[0])
    # Inferior
    for x in range(w):
        if lobby[h - 1][x] == ".":
            return x, h - 1
    # Laterales
    for y in range(h):
        if lobby[y][0] == ".":
            return 0, y
        if lobby[y][w - 1] == ".":
            return w - 1, y
    # Centro inferior forzado
    return w // 2, h - 1


class MapService:
    """
    Encapsula la lógica para generar, fusionar, conectar y cargar mapas.
    Soporta modos: 'lobby', 'dungeon', 'combined' y 'global'.
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
        width: int = DUNGEON_WIDTH,
        height: int = DUNGEON_HEIGHT,
        offset_x: int = LOBBY_OFFSET_X,
        offset_y: int = LOBBY_OFFSET_Y,
        map_mode: str = "combined",
        map_name: Optional[str] = None,
        export_debug: bool = True,
    ) -> MapData:

        # ————— MODO GLOBAL —————
        if map_mode == "global":
            key = map_name or "global_map"

            # 1️⃣ Canvas global inicializado con muros
            global_matrix: List[List[str]] = [
                ["#" for _ in range(GLOBAL_WIDTH)]
                for _ in range(GLOBAL_HEIGHT)
            ]

            # 2️⃣ Generar dungeon procedural
            raw_map, metadata = self.generator.generate(
                width=DUNGEON_WIDTH,
                height=DUNGEON_HEIGHT,
                return_rooms=True
            )

            # 3️⃣ Pegar dungeon en canvas global
            for y, row in enumerate(raw_map):
                for x, ch in enumerate(row):
                    gx = DUNGEON_OFFSET_X + x
                    gy = DUNGEON_OFFSET_Y + y
                    if 0 <= gx < GLOBAL_WIDTH and 0 <= gy < GLOBAL_HEIGHT:
                        global_matrix[gy][gx] = ch

            # 4️⃣ Generar lobby dinámico y pegar sobre global
            lobby = _generate_lobby_matrix()
            for y, row in enumerate(lobby):
                for x, ch in enumerate(row):
                    gx = LOBBY_OFFSET_X + x
                    gy = LOBBY_OFFSET_Y + y
                    if 0 <= gx < GLOBAL_WIDTH and 0 <= gy < GLOBAL_HEIGHT:
                        global_matrix[gy][gx] = ch

            # 5️⃣ Conectar lobby ↔ dungeon
            # 5.a) hallamos salida del lobby en local y global
            exit_local = _find_lobby_exit(lobby)
            exit_global = (exit_local[0] + LOBBY_OFFSET_X,
                           exit_local[1] + LOBBY_OFFSET_Y)

            # 5.b) calculamos centros de salas en global
            rooms = metadata.get("rooms", [])
            centers_global = [
                ( (r[0] + r[2]) // 2 + DUNGEON_OFFSET_X,
                  (r[1] + r[3]) // 2 + DUNGEON_OFFSET_Y )
                for r in rooms
            ]

            # 5.c) escogemos la sala más cercana (Manhattan)
            best = min(
                centers_global,
                key=lambda c: abs(c[0] - exit_global[0]) + abs(c[1] - exit_global[1])
            )

            # 5.d) trazamos túnel en 'L' usando DungeonGenerator
            ex, ey = exit_global
            bx, by = best
            if random.random() < 0.5:
                DungeonGenerator._horiz_tunnel(global_matrix, ex, bx, ey)
                DungeonGenerator._vert_tunnel (global_matrix, ey, by, bx)
            else:
                DungeonGenerator._vert_tunnel (global_matrix, ey, by, ex)
                DungeonGenerator._horiz_tunnel(global_matrix, ex, bx, by)

            # 6️⃣ Pasar a lista de strings y cargar loader
            merged_matrix = ["".join(r) for r in global_matrix]
            _, tiles, overlay = self.loader.load(merged_matrix, key)

            return MapData(
                matrix=merged_matrix,
                tiles=tiles,
                overlay=overlay,
                metadata=metadata,
                name=key,
            )

        # ——— MODOS EXISTENTES: lobby, dungeon, combined ———
        key = map_name or ("lobby_map" if map_mode == "lobby" else "combined_map")

        # Modo lobby puro
        if map_mode == "lobby":
            merged_matrix = _generate_lobby_matrix()
            metadata: Dict = {}

        else:
            # 1) Generar raw dungeon
            raw_map, metadata = self.generator.generate(
                width=width,
                height=height,
                return_rooms=(map_mode == "combined"),
                avoid_zone=(
                    offset_x,
                    offset_y + LOBBY_HEIGHT,
                    offset_x + LOBBY_WIDTH,
                    offset_y + LOBBY_HEIGHT + 3,
                ) if map_mode == "combined" else None,
            )

            # 2) Elegir matriz según modo
            if map_mode == "dungeon":
                merged_matrix = ["".join(row) for row in raw_map]
            else:  # combined
                lobby = _generate_lobby_matrix()
                rooms = metadata.get("rooms", [])
                fused = self.merger.merge(
                    handmade_map=[list(r) for r in lobby],
                    generated_map=raw_map,
                    offset_x=offset_x,
                    offset_y=offset_y,
                    dungeon_rooms=rooms,
                )
                merged_matrix = ["".join(r) for r in fused]

                if export_debug:
                    try:
                        fn = self.exporter.export(merged_matrix)
                        logger.debug("Mapa de debug exportado como %s", fn)
                    except Exception as e:
                        logger.warning("Error exportando debug: %s", e)

        # 3️⃣ Cargar matriz, tiles y overlay
        _, tiles, overlay = self.loader.load(merged_matrix, key)

        return MapData(
            matrix=merged_matrix,
            tiles=tiles,
            overlay=overlay,
            metadata=metadata,
            name=key,
        )
