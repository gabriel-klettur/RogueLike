"""
Package de gestión de mapas refactorizado.
"""
from pathlib import Path
import logging
import os
import json

from roguelike_engine.map.model.layer import Layer
from roguelike_engine.map.utils import calculate_dungeon_offset, get_zone_for_tile
from roguelike_engine.config.map_config import global_map_settings
from roguelike_engine.config import config as cfg
from roguelike_engine.config.config_tiles import TILE_SIZE
from roguelike_game.factories.player.config import RENDERED_SPRITE_SIZE
from roguelike_engine.tile.utils.assets import clear_sprite_caches
from roguelike_engine.rendering.lighting import get_global_lighting
from roguelike_engine.rendering.lighting.light_instances_loader import reset_persistent_loader

from .loader import MapLoader
from roguelike_engine.worlds.service import world_service
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
    def spawn_player(self, tile_pos: tuple[int, int]) -> None:
        """Persist tile spawn and, if it changed, rebuild cached map chunks."""
        previous_tile = self._local_state.get("player_pos")
        self._local_state["player_pos"] = tile_pos

        if tile_pos != previous_tile:
            try:
                self.view.invalidate_cache()
            except Exception:
                pass
            try:
                clear_sprite_caches()
            except Exception:
                pass

    def get_spawn_pixel(self, tile_pos: tuple[int, int]) -> tuple[int, int]:
        return tile_to_spawn_pixel(tile_pos, RENDERED_SPRITE_SIZE, TILE_SIZE)

    def restore_npc_states(self, npc_memory: dict):
        self._local_state["npc_states"].update(npc_memory)

    def serialize_state(self) -> dict:
        return self._local_state.copy()

    def deserialize_state(self, data: dict):
        self._local_state.update(data)

    def reload_map(self):
        """
        Recarga el mapa EN SITIO, manteniendo referencias (renderer, view, etc.).
        Importante para el editor: tras crear/renombrar/borrar zonas, necesitamos
        recalcular el etiquetado por zonas (tiles_by_zone) usando los offsets
        actualizados en global_map_settings.zone_offsets.

        Devuelve self para compatibilidad con llamadas existentes que ignoran
        el valor de retorno.
        """
        # Preserve current world overlay grid to avoid losing live edits during a reload
        try:
            _prev_overlay = self.layers.get(Layer.Ground)
        except Exception:
            _prev_overlay = None

        # Recargar datos base del mapa
        result = self.loader.load(self.map_name)
        self.result = result

        # Propiedades básicas
        self.name = result.name
        self.matrix = result.matrix
        self.layers = result.layers
        self.tiles_by_layer = result.tiles_by_layer
        self.overlay = self.layers.get(Layer.Ground)
        self.tiles = result.tiles
        self.solid_tiles = flatten_tiles(self.tiles)

        # Recalcular etiquetado por zonas con offsets actualizados
        self.tiles_by_zone.clear()
        for row in self.tiles:
            for tile in row:
                tx = tile.x // TILE_SIZE
                ty = tile.y // TILE_SIZE
                zone = get_zone_for_tile(tx, ty)
                tile.zone = zone
                self.tiles_by_zone.setdefault(zone, []).append(tile)

        # Actualizar colisiones con los nuevos datos
        self.collision_layers = self.collision_manager.load(self)

        # Cache auxiliar
        self.tiles_in_region = flatten_tiles(self.tiles)

        # Invalidate chunked view caches para forzar re-render
        try:
            self.view.invalidate_cache()
        except Exception:
            pass

        return self

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
        # Render passing the map model (self) to the chunked view
        self.renderer.render(surface, camera, self)

    def get_zone_for(self, row: int, col: int) -> tuple[str, int, int]:
        """Return zone name and offsets for the given tile coordinates."""
        zone = get_zone_for_tile(col, row)
        offx, offy = global_map_settings.zone_offsets.get(zone, (0, 0))
        return zone, offx, offy

    # --- Multi-world swap API -------------------------------------------------
    def swap_world_and_spawn(self, world_id: str, tile_pos: tuple[int, int] | None) -> None:
        """Cambia el mundo activo y reaparece al jugador en tile_pos.

        - Actualiza rutas (overlays/collisions/zones/buildings) vía WorldService.
        - Recarga el mapa desde el mundo activo y recalcula colisiones.
        - Reposiciona al jugador y fuerza invalidación de la vista.
        """
        # --- Structured debug trace (only on teleport) ---
        trace_id = None
        try:
            import time as _t
            trace_id = f"TP{int(_t.time()*1000)}"
            logger.info(
                f"[{trace_id}] BEGIN swap: cur_world={getattr(global_map_settings,'current_world','?')} -> dest_world={world_id} map={self.map_name}"
            )
            # Snapshot BEFORE
            try:
                overlays = list(global_map_settings.overlays_dir.glob('*.overlay.json'))
                collisions = list(global_map_settings.collisions_dir.glob('*.json'))
                bdir = global_map_settings.buildings_dir
                sdir = (global_map_settings.worlds_dir / world_id / 'spawners')
                cache_file = Path(getattr(self.loader, 'cache_dir', Path('data/cache'))) / f"map_{getattr(global_map_settings,'current_world','base')}_{self.map_name}.pkl"
                logger.info(
                    f"[{trace_id}] BEFORE: zones_index={global_map_settings.ZONES_INDEX} overlays_dir={global_map_settings.overlays_dir} overlays={len(overlays)} collisions_dir={global_map_settings.collisions_dir} collisions={len(collisions)} buildings_dir={bdir} spawners_dir={sdir} cache_exists={cache_file.exists()} zone_keys={list(getattr(global_map_settings,'zone_offsets',{}).keys())[:4]}.."
                )
            except Exception:
                pass
        except Exception:
            pass
        # Activar mundo destino y validar
        try:
            world_service.activate(world_id)
        except Exception as e:
            try:
                logger.error(f"[{trace_id}] CRITICAL: world_service.activate('{world_id}') failed: {e}")
            except Exception:
                pass
            raise  # No silenciar este error crítico
        # Validar que current_world se actualizó correctamente
        try:
            actual_world = getattr(global_map_settings, 'current_world', None)
            if actual_world != world_id:
                msg = f"World activation failed: expected '{world_id}', got '{actual_world}'"
                try:
                    logger.error(f"[{trace_id}] {msg}")
                except Exception:
                    pass
                raise RuntimeError(msg)
        except RuntimeError:
            raise
        except Exception:
            pass
        # Forzar invalidación de caches antes de recargar
        try:
            # 1) Limpiar cache de chunks/sprites de la vista actual
            try:
                self.view.invalidate_cache()
            except Exception:
                pass
            # 1b) Limpiar caches de sprites base/overlay para evitar artefactos entre mundos
            try:
                clear_sprite_caches()
            except Exception:
                pass
            # 2) Borrar cache de mapa del mundo destino y origen para evitar contaminación
            try:
                cache_dir = Path(getattr(self.loader, 'cache_dir', Path('data/cache')))
                # Borrar cache del mundo DESTINO
                dest_cache = cache_dir / f"map_{world_id}_{self.map_name}.pkl"
                if dest_cache.exists():
                    dest_cache.unlink(missing_ok=True)
                    try:
                        logger.info(f"[{trace_id}] Cleared dest cache: {dest_cache}")
                    except Exception:
                        pass
                # Borrar cache del mundo ORIGEN para prevenir carga incorrecta
                try:
                    # current_world ya fue actualizado a world_id en activate()
                    # pero guardamos una referencia al mundo previo si existe
                    for cache_file in cache_dir.glob(f"map_*_{self.map_name}.pkl"):
                        # Borrar todos los caches para este mapa excepto el que acabamos de borrar
                        if cache_file != dest_cache and cache_file.exists():
                            try:
                                cache_file.unlink(missing_ok=True)
                                logger.info(f"[{trace_id}] Cleared old cache: {cache_file}")
                            except Exception:
                                pass
                except Exception:
                    pass
            except Exception:
                pass
        except Exception:
            pass
        # Refrescar offsets y reiniciar sistemas dependientes para evitar estado residual
        try:
            global_map_settings.refresh_zone_offsets()
        except Exception:
            pass
        try:
            # Re-crear collision manager para limpiar capas previas
            self.collision_manager = CollisionManager()
        except Exception:
            pass
        # Recargar mapa con nuevas rutas
        try:
            self.reload_map()
            # CRÍTICO: Limpiar sprite caches DESPUÉS de reload para eliminar referencias al mundo anterior
            try:
                clear_sprite_caches()
                if trace_id:
                    logger.info(f"[{trace_id}] Cleared sprite caches after reload_map()")
            except Exception:
                pass
            # CRÍTICO: Re-crear renderer y vista DESPUÉS de limpiar sprites
            # para forzar reconstrucción de chunks desde cero sin sprites cacheados
            try:
                self.renderer = MapRenderer()
                self.view = self.renderer.view
                if trace_id:
                    logger.info(f"[{trace_id}] Recreated renderer and view after sprite cache clear")
            except Exception:
                pass
        except Exception:
            pass
        # Si el mundo destino está en blanco (zones.json vacío), limpiar instancias de edificios per-world
        try:
            ztxt = global_map_settings.ZONES_INDEX.read_text(encoding='utf-8').strip()
            zkeys = list(json.loads(ztxt).keys()) if ztxt else []
            if len(zkeys) == 0:
                inst_path = global_map_settings.buildings_dir / 'buildings_instances.json'
                with inst_path.open('w', encoding='utf-8') as f:
                    json.dump([], f, indent=2)
                if trace_id:
                    logger.info(f"[{trace_id}] Cleared buildings instances for blank world: {inst_path}")
        except Exception:
            pass
        # Si no hay tile_pos, usar (0,0) como spawn por defecto (mundo vacío sin zonas)
        if tile_pos is None:
            tile_pos = (0, 0)
        # Reaparecer jugador y recalcular colisiones
        try:
            self.spawn_player(tile_pos)
        except Exception:
            pass
        try:
            self.collision_manager.load(self)
        except Exception:
            pass
        # Reinicializar luces persistentes para el nuevo mundo: limpiar el
        # LightingManager global y resetear el loader para que vuelva a cargar
        # instancias usando los offsets del mundo activo.
        try:
            lm = get_global_lighting()
            try:
                lm.clear()
            except Exception:
                pass
            try:
                reset_persistent_loader()
            except Exception:
                pass
        except Exception:
            pass
        try:
            self.view.invalidate_cache()
        except Exception:
            pass
        # Snapshot AFTER
        try:
            if trace_id:
                overlays = list(global_map_settings.overlays_dir.glob('*.overlay.json'))
                collisions = list(global_map_settings.collisions_dir.glob('*.json'))
                bdir = global_map_settings.buildings_dir
                sdir = (global_map_settings.worlds_dir / world_id / 'spawners')
                cache_file = Path(getattr(self.loader, 'cache_dir', Path('data/cache'))) / f"map_{getattr(global_map_settings,'current_world','base')}_{self.map_name}.pkl"
                # zones.json size and parsed keys
                try:
                    ztxt = global_map_settings.ZONES_INDEX.read_text(encoding='utf-8').strip()
                    zsize = len(ztxt)
                    zkeys = list(json.loads(ztxt).keys()) if ztxt else []
                except Exception:
                    zsize, zkeys = -1, []
                logger.info(
                    f"[{trace_id}] AFTER: cur_world={getattr(global_map_settings,'current_world','?')} zones_index={global_map_settings.ZONES_INDEX} zsize={zsize} zkeys={zkeys} overlays_dir={global_map_settings.overlays_dir} overlays={len(overlays)} collisions_dir={global_map_settings.collisions_dir} collisions={len(collisions)} buildings_dir={bdir} spawners_dir={sdir} cache_exists={cache_file.exists()}"
                )
                logger.info(f"[{trace_id}] END swap")
        except Exception:
            pass
