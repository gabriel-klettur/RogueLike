# Path: src/roguelike_engine/map/core/service.py

import logging
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
    # ya no importamos LOBBY_WIDTH ni LOBBY_HEIGHT directamente aquí
)
from roguelike_engine.map.generator.factory import get_generator
from roguelike_engine.map.merger.factory import get_merger
from roguelike_engine.map.loader.factory import get_map_loader
from roguelike_engine.map.exporter.factory import get_exporter, MapExporter

from .model import MapData

logger = logging.getLogger(__name__)

def _generate_lobby_matrix() -> List[str]:
    """
    Genera dinámicamente el mapa del lobby en base a config_map:
    - Borde de muros '#'
    - Interior de suelo '.'
    """
    from src.roguelike_engine.config_map import LOBBY_WIDTH, LOBBY_HEIGHT

    matrix: List[str] = []
    for y in range(LOBBY_HEIGHT):
        if y == 0 or y == LOBBY_HEIGHT - 1:
            row = "#" * LOBBY_WIDTH
        else:
            row = "#" + "." * (LOBBY_WIDTH - 2) + "#"
        matrix.append(row)
    return matrix

class MapService:
    """
    Encapsula toda la lógica de negocio para generar, fusionar,
    exportar debug y cargar un mapa completo. Ahora sin dependencia
    de un archivo estático de lobby.
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
            # 1️⃣ Canvas global
            global_map: List[List[str]] = [
                ["#" for _ in range(GLOBAL_WIDTH)]
                for _ in range(GLOBAL_HEIGHT)
            ]

            # 2️⃣ Generar dungeon procedural
            raw_map, metadata = self.generator.generate(
                width=DUNGEON_WIDTH,
                height=DUNGEON_HEIGHT,
                return_rooms=True
            )

            # 3️⃣ Pegar dungeon
            for y, row in enumerate(raw_map):
                for x, ch in enumerate(row):
                    gx = DUNGEON_OFFSET_X + x
                    gy = DUNGEON_OFFSET_Y + y
                    if 0 <= gx < GLOBAL_WIDTH and 0 <= gy < GLOBAL_HEIGHT:
                        global_map[gy][gx] = ch

            # 4️⃣ Generar y pegar lobby
            lobby = _generate_lobby_matrix()
            for y, row in enumerate(lobby):
                for x, ch in enumerate(row):
                    gx = LOBBY_OFFSET_X + x
                    gy = LOBBY_OFFSET_Y + y
                    if 0 <= gx < GLOBAL_WIDTH and 0 <= gy < GLOBAL_HEIGHT:
                        global_map[gy][gx] = ch

            merged_matrix = ["".join(r) for r in global_map]
            _, tiles, overlay = self.loader.load(merged_matrix, key)
            return MapData(matrix=merged_matrix, tiles=tiles, overlay=overlay,
                           metadata=metadata, name=key)

        # ————— MODOS EXISTENTES: lobby, dungeon, combined —————
        key = map_name or ("lobby_map" if map_mode == "lobby" else "combined_map")

        # Lobby puro
        if map_mode == "lobby":
            merged_matrix = _generate_lobby_matrix()
            metadata: Dict = {}

        else:
            # Generar dungeon (simple o combinado)
            raw_map, metadata = self.generator.generate(
                width=width,
                height=height,
                return_rooms=(map_mode == "combined"),
                avoid_zone=(
                    offset_x,
                    offset_y + _generate_lobby_matrix().__len__(),  # evita zona del lobby
                    offset_x + len(_generate_lobby_matrix()[0]),
                    offset_y + _generate_lobby_matrix().__len__() + 3,
                ) if map_mode == "combined" else None,
            )
            if map_mode == "dungeon":
                merged_matrix = ["".join(row) for row in raw_map]
            else:
                # combined: fusionar dungeon + lobby
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

        # Carga final de tiles y overlay
        _, tiles, overlay = self.loader.load(merged_matrix, key)
        return MapData(matrix=merged_matrix, tiles=tiles,
                       overlay=overlay, metadata=metadata, name=key)
