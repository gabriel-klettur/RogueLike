import logging
from typing import Optional, Tuple, List, Dict

from src.roguelike_engine.config_map import (
    DUNGEON_WIDTH,
    DUNGEON_HEIGHT,
    LOBBY_OFFSET_X,
    LOBBY_OFFSET_Y,
    LOBBY_WIDTH,
    LOBBY_HEIGHT,
)
from roguelike_engine.map.generator.factory import get_generator
from roguelike_engine.map.merger.factory import get_merger
from roguelike_engine.map.loader.factory import get_map_loader
from roguelike_engine.map.exporter.factory import get_exporter, MapExporter
from data.maps.handmade_maps.lobby_map import LOBBY_MAP

from .model import MapData

logger = logging.getLogger(__name__)

class MapService:
    """
    Encapsula toda la lógica de negocio para generar, fusionar,
    exportar debug y cargar un mapa completo.
    """

    def __init__(
        self,
        generator_name: str = "dungeon",
        merger_name: str = "center_to_center",
        loader_name: str = "text",
        exporter: Optional[MapExporter] = None,
    ):
        self.generator = get_generator(generator_name)
        self.merger = get_merger(merger_name)
        self.loader = get_map_loader(loader_name)
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
        """
        Construye un MapData completo, lista para renderizar o persistir.

        :param width: ancho de la dungeon
        :param height: alto de la dungeon
        :param offset_x: desplazamiento X del lobby
        :param offset_y: desplazamiento Y del lobby
        :param map_mode: "lobby" | "dungeon" | "combined"
        :param map_name: nombre clave para overlay; si None, se inferirá
        :param export_debug: si True, exporta txt de debug
        :returns: MapData con matrix, tiles, overlay, metadata y name
        """
        # 1️⃣ Determinar clave
        key = map_name or ("lobby_map" if map_mode == "lobby" else "combined_map")

        # 2️⃣ Generar la matriz de caracteres y metadata
        if map_mode == "lobby":
            merged_matrix = LOBBY_MAP
            metadata: Dict = {}
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
                merged_matrix = ["".join(row) for row in raw_map]
            else:
                rooms = metadata.get("rooms", [])
                fused = self.merger.merge(
                    handmade_map=LOBBY_MAP,
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

        # 3️⃣ Cargar tiles y overlay definitivo
        _, tiles, overlay = self.loader.load(merged_matrix, key)

        # 4️⃣ Empaquetar y devolver
        return MapData(
            matrix=merged_matrix,
            tiles=tiles,
            overlay=overlay,
            metadata=metadata,
            name=key,
        )
