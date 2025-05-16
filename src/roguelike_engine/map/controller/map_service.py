import logging
import random
from typing import Optional, List, Tuple, Dict

from roguelike_engine.config_map import (
    GLOBAL_WIDTH,
    GLOBAL_HEIGHT,
    ZONE_WIDTH,
    ZONE_HEIGHT,
    DUNGEON_CONNECT_SIDE,
)
from roguelike_engine.map.model.generator.factory import get_generator
from roguelike_engine.map.model.loader.factory import get_map_loader
from roguelike_engine.map.model.exporter.factory import get_exporter, MapExporter
from roguelike_engine.map.model.map_model import Map
from roguelike_engine.map.utils import (
    generate_lobby_matrix,
    calculate_lobby_offset,
    calculate_dungeon_offset,
    find_lobby_exit,
)
from roguelike_engine.map.model.generator.dungeon import DungeonGenerator

logger = logging.getLogger(__name__)

class MapService:
    """
    Servicio para generación, carga y fusión de mapas en modos:
    'lobby', 'dungeon', 'combined' y 'global'.
    """

    def __init__(
        self,
        generator_name: str = "dungeon",
        loader_name: str = "text",
        exporter: Optional[MapExporter] = None,
    ):
        self.generator = get_generator(generator_name)
        self.loader = get_map_loader(loader_name)
        self.exporter: MapExporter = exporter or get_exporter("debug_txt")

    def build_map(self, map_name: Optional[str] = None) -> Map:
        """
        Orquesta la creación completa del mapa:
        1) Inicializa el canvas global.
        2) Coloca el lobby.
        3) Genera y posiciona la dungeon.
        4) Conecta túneles entre lobby y dungeon.
        5) Carga tiles y overlay.
        """
        key = map_name or "global_map"

        canvas = self._create_canvas()
        lobby_offset = self._place_lobby(canvas)
        dungeon_info = self._place_dungeon(canvas, lobby_offset)
        self._connect_tunnels(canvas, lobby_offset, dungeon_info)
        rows, tiles, overlay = self._load_map(canvas, key)

        metadata = dungeon_info["metadata"]
        metadata["lobby_offset"] = lobby_offset

        return Map(rows, tiles, overlay, metadata, key)

    def _create_canvas(self) -> List[List[str]]:
        """
        Crea un canvas global lleno de muros ('#').
        """
        return [["#" for _ in range(GLOBAL_WIDTH)] for __ in range(GLOBAL_HEIGHT)]

    def _place_lobby(self, canvas: List[List[str]]) -> Tuple[int, int]:
        """
        Genera la matriz del lobby y la inserta centrada en el canvas.
        Devuelve el offset (ox, oy) donde se colocó.
        """
        lobby = generate_lobby_matrix()
        ox, oy = calculate_lobby_offset()
        for y, row in enumerate(lobby):
            for x, ch in enumerate(row):
                canvas[oy + y][ox + x] = ch
        return ox, oy

    def _place_dungeon(
        self,
        canvas: List[List[str]],
        lobby_offset: Tuple[int, int]
    ) -> Dict[str, object]:
        """
        Genera la dungeon procedural y la superpone en el canvas.
        Retorna un dict con 'offset' y 'metadata'.
        """
        raw_map, metadata = self.generator.generate(
            width=ZONE_WIDTH,
            height=ZONE_HEIGHT,
            return_rooms=True,
        )
        dx, dy = calculate_dungeon_offset(lobby_offset, DUNGEON_CONNECT_SIDE)
        for y, row in enumerate(raw_map):
            for x, ch in enumerate(row):
                gx, gy = dx + x, dy + y
                if 0 <= gx < GLOBAL_WIDTH and 0 <= gy < GLOBAL_HEIGHT:
                    canvas[gy][gx] = ch
        return {"offset": (dx, dy), "metadata": metadata}

    def _connect_tunnels(
        self,
        canvas: List[List[str]],
        lobby_offset: Tuple[int, int],
        dungeon_info: Dict[str, object]
    ) -> None:
        """
        Conecta el lobby y la dungeon mediante túneles.
        """
        rooms = dungeon_info["metadata"].get("rooms", [])
        if not rooms:
            return

        dx, dy = dungeon_info["offset"]
        # Punto de salida en el lobby
        exit_local = find_lobby_exit(generate_lobby_matrix(), DUNGEON_CONNECT_SIDE)
        ex = lobby_offset[0] + exit_local[0]
        ey = lobby_offset[1] + exit_local[1]

        # Centros de habitaciones
        centers = [
            ((r[0] + r[2]) // 2 + dx, (r[1] + r[3]) // 2 + dy)
            for r in rooms
        ]
        bx, by = min(centers, key=lambda c: abs(c[0] - ex) + abs(c[1] - ey))

        # Túneles horizontales/verticales alternados
        if random.random() < 0.5:
            DungeonGenerator._horiz_tunnel(canvas, ex, bx, ey)
            DungeonGenerator._vert_tunnel(canvas, ey, by, bx)
        else:
            DungeonGenerator._vert_tunnel(canvas, ey, by, ex)
            DungeonGenerator._horiz_tunnel(canvas, ex, bx, by)

    def _load_map(
        self,
        canvas: List[List[str]],
        key: str
    ) -> Tuple[List[str], List[List[object]], Optional[List[List[str]]]]:
        """
        Serializa el canvas a strings, carga tiles y overlay.
        """
        rows = ["".join(r) for r in canvas]
        _, tiles, overlay = self.loader.load(rows, key)
        return rows, tiles, overlay
