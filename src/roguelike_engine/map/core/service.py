# Path: src/roguelike_engine/map/core/service.py

import logging
import random
from typing import Optional, Tuple, List, Dict

from src.roguelike_engine.config_map import (
    GLOBAL_WIDTH,
    GLOBAL_HEIGHT,
    DUNGEON_WIDTH,
    DUNGEON_HEIGHT,
    DUNGEON_OFFSET_X,
    DUNGEON_OFFSET_Y,
    LOBBY_WIDTH,
    LOBBY_HEIGHT,
    LOBBY_OFFSET_X,
    LOBBY_OFFSET_Y,
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


def _find_lobby_exit(lobby: List[str]) -> Tuple[int, int]:
    """
    Encuentra un punto de salida en el lobby:
    1) Busca '.' en la fila inferior
    2) Si no hay, busca en paredes laterales
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


def _calculate_lobby_offset() -> Tuple[int, int]:
    """
    Calcula el offset (x,y) en el lienzo GLOBAL donde ubicar el lobby
    de tamaño LOBBY_WIDTH×LOBBY_HEIGHT de modo que quede en la celda
    central de un grid de nodos de ese tamaño.
    """
    n_cols = GLOBAL_WIDTH // LOBBY_WIDTH
    n_rows = GLOBAL_HEIGHT // LOBBY_HEIGHT

    # Si no cabe entero, se centra libremente
    if n_cols < 1 or n_rows < 1:
        return (
            (GLOBAL_WIDTH - LOBBY_WIDTH) // 2,
            (GLOBAL_HEIGHT - LOBBY_HEIGHT) // 2,
        )

    center_col = n_cols // 2
    center_row = n_rows // 2

    rem_x = GLOBAL_WIDTH - n_cols * LOBBY_WIDTH
    rem_y = GLOBAL_HEIGHT - n_rows * LOBBY_HEIGHT

    start_x = rem_x // 2
    start_y = rem_y // 2

    offset_x = start_x + center_col * LOBBY_WIDTH
    offset_y = start_y + center_row * LOBBY_HEIGHT

    return offset_x, offset_y


class MapService:
    """
    Encapsula toda la lógica de negocio para generar, fusionar,
    conectar y cargar un mapa completo. Soporta modos:
    'lobby', 'dungeon', 'combined' y 'global'.
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
        offset_x: int = LOBBY_OFFSET_X,
        offset_y: int = LOBBY_OFFSET_Y,
        map_mode: str = "combined",
        map_name: Optional[str] = None,
        export_debug: bool = True,
    ) -> MapData:

        # ————— MODO GLOBAL —————
        if map_mode == "global":
            key = map_name or "global_map"

            # 1️⃣ Canvas global lleno de muros
            global_map: List[List[str]] = [
                ["#" for _ in range(GLOBAL_WIDTH)]
                for _ in range(GLOBAL_HEIGHT)
            ]

            # 2️⃣ Generar dungeon procedural
            raw_map, metadata = self.generator.generate(
                width=DUNGEON_WIDTH,
                height=DUNGEON_HEIGHT,
                return_rooms=True,
            )

            # 3️⃣ Pegar dungeon en canvas global
            for y, row in enumerate(raw_map):
                for x, ch in enumerate(row):
                    gx = DUNGEON_OFFSET_X + x
                    gy = DUNGEON_OFFSET_Y + y
                    if 0 <= gx < GLOBAL_WIDTH and 0 <= gy < GLOBAL_HEIGHT:
                        global_map[gy][gx] = ch

            # 4️⃣ Generar lobby dinámico
            lobby = _generate_lobby_matrix()
            lobby_off_x, lobby_off_y = _calculate_lobby_offset()

            # 5️⃣ Pegar lobby
            for y, row in enumerate(lobby):
                for x, ch in enumerate(row):
                    gx = lobby_off_x + x
                    gy = lobby_off_y + y
                    if 0 <= gx < GLOBAL_WIDTH and 0 <= gy < GLOBAL_HEIGHT:
                        global_map[gy][gx] = ch

            # 6️⃣ Conectar lobby ↔ dungeon
            exit_local = _find_lobby_exit(lobby)
            ex = exit_local[0] + lobby_off_x
            ey = exit_local[1] + lobby_off_y

            rooms = metadata.get("rooms", [])
            centers = [
                (((r[0] + r[2]) // 2) + DUNGEON_OFFSET_X,
                 ((r[1] + r[3]) // 2) + DUNGEON_OFFSET_Y)
                for r in rooms
            ]
            if centers:
                bx, by = min(
                    centers,
                    key=lambda c: abs(c[0] - ex) + abs(c[1] - ey),
                )
                if random.random() < 0.5:
                    DungeonGenerator._horiz_tunnel(global_map, ex, bx, ey)
                    DungeonGenerator._vert_tunnel(global_map, ey, by, bx)
                else:
                    DungeonGenerator._vert_tunnel(global_map, ey, by, ex)
                    DungeonGenerator._horiz_tunnel(global_map, ex, bx, by)

            # 7️⃣ Convertir y cargar
            merged = ["".join(r) for r in global_map]
            _, tiles, overlay = self.loader.load(merged, key)
            return MapData(
                matrix=merged,
                tiles=tiles,
                overlay=overlay,
                metadata=metadata,
                name=key,
            )

        # ——— MODOS EXISTENTES: lobby, dungeon, combined ———
        key = map_name or ("lobby_map" if map_mode == "lobby" else "combined_map")

        # Lobby puro
        if map_mode == "lobby":
            merged = _generate_lobby_matrix()
            metadata = {}
        else:
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

            if map_mode == "dungeon":
                merged = ["".join(row) for row in raw_map]
            else:  # combined
                lobby = _generate_lobby_matrix()
                fused = self.merger.merge(
                    handmade_map=[list(r) for r in lobby],
                    generated_map=raw_map,
                    offset_x=offset_x,
                    offset_y=offset_y,
                    dungeon_rooms=metadata.get("rooms", []),
                )
                merged = ["".join(r) for r in fused]
                if export_debug:
                    try:
                        fn = self.exporter.export(merged)
                        logger.debug("Mapa de debug exportado como %s", fn)
                    except Exception as e:
                        logger.warning("Error exportando debug: %s", e)

        # Cargar matriz, tiles y overlay
        _, tiles, overlay = self.loader.load(merged, key)
        return MapData(
            matrix=merged,
            tiles=tiles,
            overlay=overlay,
            metadata=metadata,
            name=key,
        )
