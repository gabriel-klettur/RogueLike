# Path: src/roguelike_game/game/map_manager.py
from roguelike_engine.map.controller.map_controller import build_map
from roguelike_engine.map.utils import calculate_dungeon_offset
from roguelike_engine.map.utils import get_zone_for_tile
from roguelike_engine.map.view.chunked_map_view import ChunkedMapView

from roguelike_engine.config.config_tiles import TILE_SIZE

class MapManager:
    def __init__(self, map_name: str | None):
        # Guardar nombre para recargas dinámicas
        self.map_name = map_name
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
        # rooms de cada zona (usado para conectar túneles)
        self.zone_rooms: dict[str, list] = {}
        self.zone_rooms["dungeon"] = self.rooms

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

    def reload_map(self):
        """
        Reconstruye el mapa usando la configuración actual (additional_zones, auto_expand).
        """
        # Vuelve a inicializar el MapManager con el mismo map_name
        self.__init__(self.map_name)

    def expand_zone(self, side: str, zone_key: str, parent_key: str) -> None:
        """
        Agrega una zona nueva al mapa existente de forma incremental.
        side: 'bottom','top','left','right'
        """
        from roguelike_engine.map.model.generator.factory import get_generator
        from roguelike_engine.config.map_config import global_map_settings
        from roguelike_engine.map.model.loader.text_loader_strategy import TextMapLoader

        # Guardar offsets viejos
        old_offsets = global_map_settings.zone_offsets.copy()
        # Limpiar cache de offsets y recalcular
        global_map_settings.__dict__.pop('zone_offsets', None)
        new_offsets = global_map_settings.zone_offsets

        # Generar nueva zona
        gen = get_generator()
        raw_map, metadata_zone = gen.generate(
            width=global_map_settings.zone_width,
            height=global_map_settings.zone_height,
            return_rooms=True
        )
        zone_matrix = [''.join(row) for row in raw_map]

        # Construir nueva matriz global de caracteres
        old_matrix = self.matrix
        old_h = len(old_matrix)
        old_w = len(old_matrix[0]) if old_h else 0
        new_h = global_map_settings.global_height
        new_w = global_map_settings.global_width

        # Iniciar grid con muros
        grid = [['#' for _ in range(new_w)] for _ in range(new_h)]

        # Calcular desplazamiento en tiles
        dx = new_offsets[parent_key][0] - old_offsets[parent_key][0]
        dy = new_offsets[parent_key][1] - old_offsets[parent_key][1]

        # Insertar antigua matriz
        for y in range(old_h):
            for x in range(old_w):
                grid[y + dy][x + dx] = old_matrix[y][x]

        # Pegar nueva zona
        off_x, off_y = new_offsets[zone_key]
        for ry, row in enumerate(zone_matrix):
            for rx, ch in enumerate(row):
                grid[off_y + ry][off_x + rx] = ch

        # Registrar rooms de la nueva zona
        self.zone_rooms[zone_key] = metadata_zone.get("rooms", [])

        # Conectar zona con su padre: túnel entre rooms más cercanas
        from roguelike_engine.map.model.generator.dungeon import DungeonGenerator
        import random
        parent_rooms = self.zone_rooms.get(parent_key, [])
        new_rooms = self.zone_rooms.get(zone_key, [])
        if parent_rooms and new_rooms:
            # Calcular centros absolutos de cada room usando offsets nuevos
            parent_centers = [((r[0]+r[2])//2 + new_offsets[parent_key][0], (r[1]+r[3])//2 + new_offsets[parent_key][1]) for r in parent_rooms]
            new_centers = [((r[0]+r[2])//2 + new_offsets[zone_key][0], (r[1]+r[3])//2 + new_offsets[zone_key][1]) for r in new_rooms]
            # Encontrar par de centros más cercano
            min_pair = None
            min_dist = float('inf')
            for pc in parent_centers:
                for nc in new_centers:
                    d = abs(pc[0]-nc[0]) + abs(pc[1]-nc[1])
                    if d < min_dist:
                        min_dist = d
                        min_pair = (pc, nc)
            if min_pair:
                (px, py), (nx, ny) = min_pair
                # Dibujar túneles en grid
                if random.random() < 0.5:
                    DungeonGenerator._horiz_tunnel(grid, px, nx, py)
                    DungeonGenerator._vert_tunnel(grid, py, ny, nx)
                else:
                    DungeonGenerator._vert_tunnel(grid, py, ny, px)
                    DungeonGenerator._horiz_tunnel(grid, px, nx, ny)

        # Actualizar matriz de strings con túneles
        self.matrix = [''.join(r) for r in grid]

        # Regenerar tiles y overlay
        loader = TextMapLoader()
        _, self.tiles, self.overlay = loader.load(self.matrix, self.name)

        # Recalcular tiles sólidos
        self.solid_tiles = [t for row in self.tiles for t in row if getattr(t, "solid", False)]

        # Reetiquetar tiles por zona
        from roguelike_engine.map.utils import get_zone_for_tile
        self.tiles_by_zone.clear()
        for row in self.tiles:
            for tile in row:
                tx = tile.x // TILE_SIZE
                ty = tile.y // TILE_SIZE
                zone = get_zone_for_tile(tx, ty)
                tile.zone = zone
                self.tiles_by_zone.setdefault(zone, []).append(tile)

        # Reset region y cache de vista
        self.tiles_in_region = self.all_tiles
        self.view.invalidate_cache()