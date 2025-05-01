# Path: src/roguelike_engine/map/manager.py

import logging
from typing import Tuple, List, Optional

from src.roguelike_engine.config_map import (
    DUNGEON_WIDTH,
    DUNGEON_HEIGHT,
    LOBBY_OFFSET_X,
    LOBBY_OFFSET_Y,
    LOBBY_WIDTH,
    LOBBY_HEIGHT,
)
from roguelike_engine.map.generator.dungeon_generator import generate_dungeon_map
from roguelike_engine.map.merger.merger import merge_handmade_with_generated
from data.maps.handmade_maps.lobby_map import LOBBY_MAP
from src.roguelike_engine.map.loader.tile_loader import load_map_from_text
from roguelike_engine.map.overlay.overlay_manager import load_overlay
from roguelike_engine.map.exporter.factory import get_exporter, MapExporter

logger = logging.getLogger(__name__)


class MapManager:
    """
    Orquesta la construcción completa de mapas: generación, fusión, carga y exportación.
    """

    def __init__(
        self,
        generator: str = "dungeon",
        merger: str = "center_to_center",
        exporter: Optional[MapExporter] = None
    ):
        self.generator_name = generator
        self.merger_name = merger
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
    ) -> Tuple[List[str], List, List, str]:
        """
        Construye el mapa y su capa overlay “permanente”.

        :param width: ancho de la dungeon
        :param height: alto de la dungeon
        :param offset_x: desplazamiento X del lobby
        :param offset_y: desplazamiento Y del lobby
        :param map_mode: "lobby", "dungeon" o "combined"
        :param map_name: clave para overlay; si None, 'lobby_map' o 'combined_map'
        :param export_debug: exportar mapa de debug si True
        :returns: merged_map, tiles, overlay_map, key
        """
        # Clave base
        key = map_name or ("lobby_map" if map_mode == "lobby" else "combined_map")

        # 1) Generación
        if map_mode == "lobby":
            merged_map = LOBBY_MAP

        elif map_mode == "dungeon":
            dungeon_map = generate_dungeon_map(width, height)
            merged_map = ["".join(row) for row in dungeon_map]

        elif map_mode == "combined":
            avoid_zone = (
                offset_x,
                offset_y + LOBBY_HEIGHT,
                offset_x + LOBBY_WIDTH,
                offset_y + LOBBY_HEIGHT + 3,
            )
            dungeon_map, dungeon_rooms = generate_dungeon_map(
                width, height, return_rooms=True, avoid_zone=avoid_zone
            )

            if offset_x + LOBBY_WIDTH > width or offset_y + LOBBY_HEIGHT > height:
                raise ValueError("El lobby no cabe en el mapa generado.")

            merged_map = merge_handmade_with_generated(
                LOBBY_MAP,
                dungeon_map,
                offset_x=offset_x,
                offset_y=offset_y,
                merge_mode=self.merger_name,
                dungeon_rooms=dungeon_rooms,
            )

            if export_debug:
                try:
                    filename = self.exporter.export(merged_map)
                    logger.debug("Mapa de debug exportado como %s", filename)
                except Exception as e:
                    logger.warning("No se pudo exportar mapa de debug: %s", e)

        else:
            raise ValueError(f"Modo de mapa no reconocido: {map_mode!r}")

        # 2) Overlay
        overlay_map = load_overlay(key)

        # 3) Carga de Tiles
        tiles = load_map_from_text(merged_map, overlay_map)

        return merged_map, tiles, overlay_map, key


# Instancia global por conveniencia
_default_manager = MapManager()


def build_map(
    *,
    width: int = DUNGEON_WIDTH,
    height: int = DUNGEON_HEIGHT,
    offset_x: int = LOBBY_OFFSET_X,
    offset_y: int = LOBBY_OFFSET_Y,
    map_mode: str = "combined",
    map_name: Optional[str] = None,
    export_debug: bool = True,
) -> Tuple[List[str], List, List, str]:
    """
    API simplificada: delega en el MapManager por defecto.
    """
    return _default_manager.build_map(
        width=width,
        height=height,
        offset_x=offset_x,
        offset_y=offset_y,
        map_mode=map_mode,
        map_name=map_name,
        export_debug=export_debug,
    )
