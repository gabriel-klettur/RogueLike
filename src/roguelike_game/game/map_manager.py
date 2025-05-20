# Path: src/roguelike_game/game/map_manager.py
from roguelike_engine.map.controller.map_controller import build_map
from roguelike_engine.map.utils import calculate_dungeon_offset
from roguelike_engine.map.utils import get_zone_for_tile
from roguelike_engine.map.view.chunked_map_view import ChunkedMapView

from roguelike_engine.config.config_tiles import TILE_SIZE

class MapManager:
    def __init__(self, map_name: str | None):
        # 1) Construir datos con MapService
        self.result = build_map(map_name)

        # 2) Propiedades básicas
        self.name = self.result.name
        self.matrix = self.result.matrix
        self.overlay = self.result.overlay
        self.tiles = self.result.tiles
        # Precomputar tiles sólidos para colisiones
        self.solid_tiles = [tile for row in self.tiles for tile in row if getattr(tile, "solid", False)]

        # 3) Offset y rooms
        self.lobby_offset = self.result.metadata.get("lobby_offset", (0, 0))
        self.rooms = self.result.metadata.get("rooms", [])

        # 4) Offset de la dungeon (en tiles)
        lob_x, lob_y = self.lobby_offset
        self.dungeon_offset = calculate_dungeon_offset((lob_x, lob_y))

        # 5) Flat list y vista chunked (se usa si necesitas seguir con chunked)
        self.tiles_in_region = self.all_tiles
        self.view = ChunkedMapView()

        # 6) Etiquetado de zona por tile
        self.tiles_by_zone: dict[str, list] = {}
        for row in self.tiles:
            for tile in row:
                tx = tile.x // TILE_SIZE
                ty = tile.y // TILE_SIZE
                zone = get_zone_for_tile(tx, ty)
                tile.zone = zone
                self.tiles_by_zone.setdefault(zone, []).append(tile)

        
        # Estado local del nivel (se usa para persistencia)
        self._local_state: dict = {
            "player_pos": None,    # Tuple[int,int] de posición de jugador en tiles
            "npc_states": {},      # dict[npc_id, estado serializado]
            # Aquí puedes añadir más claves: cofres, puertas, triggers…
        }    
        
    @property
    def all_tiles(self):
        """
        Devuelve todas las tiles del mapa en una lista plana.
        """
        return [tile for row in self.tiles for tile in row]

    # ─── Funciones de serialización / restauración ─────────────────────

    def spawn_player(self, tile_pos: tuple[int, int]):
        """
        Registra la nueva posición de jugador en coordenadas de tile.
        La posición real debe ajustarse en EntitiesManager o desde quien controle al jugador.
        """
        self._local_state["player_pos"] = tile_pos

    def restore_npc_states(self, npc_memory: dict):
        """
        Actualiza _local_state['npc_states'] con el diccionario global `npc_memory`.
        La aplicación real de estado a cada NPC se haría en EntitiesManager.
        """
        self._local_state["npc_states"].update(npc_memory)

    def serialize_state(self) -> dict:
        """
        Extrae el estado serializable de este nivel: posición de jugador y NPCs.
        """
        # Opcional: antes de serializar, podrías actualizar player_pos
        # desde las coordenadas reales de EntitiesManager.
        return self._local_state.copy()

    def deserialize_state(self, data: dict):
        """
        Aplica un estado restaurado al nivel: actualiza player_pos y npc_states.
        """
        self._local_state.update(data)
        # Opcional: disparar spawn_player / restore_npc_states para efectos inmediatos.