
# Path: src/roguelike_game/game/entities_manager.py
from typing import Tuple, List

from roguelike_game.entities.load_entities import load_entities
from roguelike_game.entities.load_hostile import load_hostile
from roguelike_game.game.map_manager import MapManager
from roguelike_engine.config_tiles import TILE_SIZE
from roguelike_engine.config_map import ZONE_OFFSETS
from roguelike_engine.map.controller.map_service import calculate_dungeon_offset
from roguelike_game.config_entities import ENEMY_MAX_UPDATE_DISTANCE
from roguelike_game.entities.npc.types.elite.controller import EliteController

class EntitiesManager:
    """
    Carga y mantiene entidades del juego: jugador, obstáculos, edificios y enemigos.
    """
    
    def __init__(self, z_state, game_map: MapManager):
        self.z_state = z_state
        self.map = game_map
        
        self.player = None
        self.obstacles = []
        self.buildings = []
        self.enemies = []        

        self.init_statics()
        self.init_enemies()        

    def init_statics(self):
        """
        Carga jugador, obstáculos y edificios.
        Devuelve (player, obstacles, buildings).
        """        
        self.player, self.obstacles, self.buildings = load_entities(self.z_state)
        self.recalibrate_buildings()
        
        return self.player, self.obstacles, self.buildings    

    def init_enemies(self):
        """
        Carga enemigos en la dungeon.
        """
        # 2️⃣ Calcular offset en tiles de la dungeon
        lob_x, lob_y = self.map.lobby_offset
        dungeon_offset = calculate_dungeon_offset((lob_x, lob_y))
        # 4️⃣ Spawn procedural de enemigos
        self.enemies = self.spawn_enemies(dungeon_offset)

    def spawn_enemies(self, dungeon_offset: Tuple[int, int]) -> List:
        """
        Genera y devuelve la lista de enemigos usando metadata del mapa.
        """
        # Coordenada inicial del jugador en tiles
        px = int(self.player.x) // TILE_SIZE
        py = int(self.player.y) // TILE_SIZE
        player_tile = (px, py)

        rooms = self.map.rooms
        self.enemies = load_hostile(
            rooms,
            player_tile,
            dungeon_offset,
            self.map.tiles
        )
        return self.enemies


    def recalibrate_buildings(self):
        """
        Actualiza el rect de colisión/render de cada edificio,
        usando las propiedades x,y derivadas de rel_x/rel_y y zone.
        """
        for b in self.buildings:
            if getattr(b, "zone", None) is not None and getattr(b, "rel_x", None) is not None:
                # b.x y b.y son propiedades que devuelven la posición absoluta
                abs_x, abs_y = b.x, b.y
                if hasattr(b, "rect"):
                    b.rect.topleft = (abs_x, abs_y)

    def update(self, state, game_map, systems):
        """
        Actualiza todas las entidades de la partida:
          - Edificios (si tuvieran lógica de update)
          - Obstáculos
          - Jugador
          - Enemigos (normales y élites)
        """
        # 1) Lógica de edición (si aplica)
        # … si tiles_editor o buildings_editor, lo sacas aquí …

        # 2) Cámara y sistemas ya los maneja update_game

        # 3) Actualizar jugador
        self.player.update(game_map)

        # 4) Actualizar obstáculos (si tuvieran update)
        for ob in self.obstacles:
            if hasattr(ob, "update"):
                ob.update(state, game_map)

        # 5) Actualizar edificios (por si los migras a ECS luego)
        for b in self.buildings:
            if hasattr(b, "update"):
                b.update(state, game_map)

        # 6) Actualizar enemigos
        # Aprovechamos el throttle y la separación Elite vs normales:
        normals, elites = [], []
        px, py = self.player.x, self.player.y
        max_d2 = ENEMY_MAX_UPDATE_DISTANCE ** 2
        for e in self.enemies:
            dx, dy = e.x - px, e.y - py
            if dx*dx + dy*dy > max_d2:
                continue
            ctrl = getattr(e, "controller", None)
            if isinstance(ctrl, EliteController):
                elites.append(ctrl)
            else:
                normals.append(e)

        for e in normals:
            e.update(state, game_map, self)
        for ctrl in elites:
            ctrl.update(state, game_map, self, systems.effects, systems.explosions)