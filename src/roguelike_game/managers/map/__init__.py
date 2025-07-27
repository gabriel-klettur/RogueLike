"""
Package de gestión de mapas refactorizado.
"""
from pathlib import Path
import logging

from roguelike_engine.map.model.layer import Layer
from roguelike_engine.map.utils import calculate_dungeon_offset, get_zone_for_tile
from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_game.factories.player.config import RENDERED_SPRITE_SIZE

from .loader import MapLoader
from .generator import MapGenerator
from .collision import CollisionManager
from .pathfinding import PathFinder
from .renderer import MapRenderer
from .utils import flatten_tiles, tile_to_spawn_pixel

logger = logging.getLogger(__name__)
logger.setLevel(logging.INFO)

class MapManager:
    """
    Orquesta la carga, generación, colisiones y renderizado del mapa.
    """
    def __init__(self, map_name: str | None):
        self.map_name = map_name
        # Loader
        self.loader = MapLoader()
        self.result = self.loader.load(map_name)

        # Propiedades básicas
        self.name = self.result.name
        self.matrix = self.result.matrix
        self.layers = self.result.layers
        self.tiles_by_layer = self.result.tiles_by_layer
        self.overlay = self.layers.get(Layer.Ground)
        self.tiles = self.result.tiles
        self.solid_tiles = flatten_tiles(self.tiles)

        # Offsets y salas
        self.lobby_offset = self.result.metadata.get("lobby_offset", (0, 0))
        self.rooms = self.result.metadata.get("rooms", [])
        self.zone_rooms: dict[str, list] = {"dungeon": self.rooms}
        self.dungeon_offset = calculate_dungeon_offset(self.lobby_offset)

        # Vista chunked y renderizador
        self.renderer = MapRenderer()
        self.view = self.renderer.view
        self.tiles_in_region = flatten_tiles(self.tiles)

        # Etiquetado por zonas
        self.tiles_by_zone: dict[str, list] = {}
        for row in self.tiles:
            for tile in row:
                tx = tile.x // TILE_SIZE
                ty = tile.y // TILE_SIZE
                zone = get_zone_for_tile(tx, ty)
                tile.zone = zone
                self.tiles_by_zone.setdefault(zone, []).append(tile)

        # Colisiones
        self.collision_manager = CollisionManager()
        self.collision_layers = self.collision_manager.load(self)

        # Estado local
        self._local_state: dict = {"player_pos": None, "npc_states": {}}

    @property
    def all_tiles(self) -> list:
        return flatten_tiles(self.tiles)

    # Serialización / estado
    def spawn_player(self, tile_pos: tuple[int, int]):
        self._local_state["player_pos"] = tile_pos

    def get_spawn_pixel(self, tile_pos: tuple[int, int]) -> tuple[int, int]:
        return tile_to_spawn_pixel(tile_pos, RENDERED_SPRITE_SIZE, TILE_SIZE)

    def restore_npc_states(self, npc_memory: dict):
        self._local_state["npc_states"].update(npc_memory)

    def serialize_state(self) -> dict:
        return self._local_state.copy()

    def deserialize_state(self, data: dict):
        self._local_state.update(data)

    def reload_map(self):
        return type(self)(self.map_name)

    def save_cache(self):
        # Forzar recacheo
        self.loader.load(self.map_name)

    def expand_zone(self, side: str, zone_key: str, parent_key: str) -> None:
        MapGenerator().expand_zone(self, side, zone_key, parent_key)

    def is_walkable(self, x: int, y: int) -> bool:
        return self.collision_manager.is_walkable(x, y)

    def find_path(self, start: tuple[int, int], goal: tuple[int, int]) -> list:
        return PathFinder().find(start, goal)

    def render(self, surface, camera) -> None:
        self.renderer.render(surface, camera, self.tiles)

    def get_zone_for(self, row: int, col: int) -> tuple[str, int, int]:
        """Return zone name and offsets for the given tile coordinates."""
        zone = get_zone_for_tile(col, row)
        offx, offy = global_map_settings.zone_offsets.get(zone, (0, 0))
        return zone, offx, offy
