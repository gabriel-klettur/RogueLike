from .component_registry import create_empty_component_store
from .system_registry import get_update_system_classes, get_render_system_classes
from .spatial_index import SpatialIndex
from roguelike_engine.utils.benchmark.benchmark_groups import BenchmarkGroup
import roguelike_engine.config.config as config
import os
from roguelike_game.ecs.systems.input.input_system import InputSystem
from roguelike_game.ecs.systems.inventory.inventory_pickup_system import InventoryPickupSystem
from roguelike_game.ecs.systems.inventory.inventory_drop_system import InventoryDropSystem

import logging
logger = logging.getLogger(__name__)

class ECSWorld:
    # Hook for override class (tests)
    ECSWorld = None

    def spawn_player_tile(self, tile_x: int, tile_y: int) -> int:
        """Legacy stub for player spawning; overridden in tests via monkeypatch."""
        raise NotImplementedError("spawn_player_tile not implemented")


    def __init__(self, screen, map_manager, buildings, perf_log=None):
        self.perf_log = perf_log
        self.screen = screen
        self.map_manager = map_manager
        self.buildings = buildings
        # Control de verbosidad para reconstrucciones (evita spam en intervalos)
        self._log_rebuild_info: bool = False

        self.entities: set[int] = set()
        self.next_entity_id = 1

        # 1) Inicializar índice espacial
        self.spatial_index = SpatialIndex(map_manager, buildings)
        # Flag para evitar reconstruir SpatialIndex cada frame
        self._spatial_index_dirty = False

        # 2) Registrar componentes
        self.components = create_empty_component_store()
        
        # Contador de frames para caches de optimización
        self._frame_count: int = 0

        # 3) Instanciar sistemas (update + render)
        self._init_systems()

    @property
    def player_position(self):
        return self.components['Position'].get(self.player_entity)

    def _init_systems(self):
        # Obtener las clases de sistemas
        update_classes = get_update_system_classes()
        render_classes = get_render_system_classes()

        # Instanciar cada sistema, inyectando config_path solo en InputSystem
        config_path = os.path.join(os.getcwd(), 'data', 'config', 'input_bindings.json')
        self.update_systems = []
        for cls in update_classes:
            if cls is InputSystem:
                inst = cls(self.perf_log, config_path)
            elif cls in (InventoryPickupSystem, InventoryDropSystem):
                inst = cls()
            else:
                inst = cls(self.perf_log)
            self.update_systems.append(inst)
        self.render_systems = [cls(self.perf_log) for cls in render_classes]
        logger.debug(f" Update systems: {[type(s).__name__ for s in self.update_systems]}")
        logger.debug(f" Render systems: {[type(s).__name__ for s in self.render_systems]}")
        # Pre-build benchmark-wrapped callables (avoids recreating closures every frame)
        self._update_callables = self._build_benchmarked_callables(
            self.update_systems, "5", "[UPDATE]"
        )
        self._render_callables = self._build_benchmarked_callables(
            self.render_systems, "4", "[RENDER]"
        )

    def _build_benchmarked_callables(self, systems, prefix, tag):
        """Pre-build a list of (system, benchmarked_fn) tuples.

        Each benchmarked_fn wraps system.update with time.perf_counter
        and appends to perf_log. Built once at init, reused every frame.
        """
        import time as _time
        callables = []
        for idx, system in enumerate(systems, 1):
            name = type(system).__name__
            key = f"{prefix}.{idx:02d}.{tag}{name}"
            perf_log = self.perf_log
            # Lightweight wrapper: just timing + append, no inspect/bind_partial
            def _make_fn(sys=system, k=key, pl=perf_log):
                def _benchmarked_update(world, *args):
                    t0 = _time.perf_counter()
                    sys.update(world, *args)
                    elapsed = _time.perf_counter() - t0
                    if pl is not None:
                        lst = pl.setdefault(k, [])
                        lst.append(elapsed)
                        if len(lst) > 300:
                            del lst[:-300]
                return _benchmarked_update
            callables.append((system, _make_fn()))
        return callables

    def reinit_systems_preserving_state(self):
        """Reinstancia los sistemas de ECS manteniendo el estado del mundo.

        Útil tras hot-reload de código para que las instancias de sistemas
        apunten a las definiciones de clase actualizadas. No toca entidades,
        componentes ni índices espaciales.
        """
        try:
            self._init_systems()
            try:
                logger.info("[ECSWorld] Systems reinitialized after hot-reload")
            except Exception:
                pass
        except Exception:
            # Nunca romper el juego por reinit fallido
            try:
                logger.exception("[ECSWorld] Failed to reinitialize systems after hot-reload")
            except Exception:
                pass

    def create_entity(self):
        eid = self.next_entity_id
        self.next_entity_id += 1
        self.entities.add(eid)
        return eid

    def get_entities_with(self, *component_types):
        if not component_types:
            return
        comps = self.components
        # Elegir componente con menos entradas para iterar sobre él
        dicts = [comps.get(ct, {}) for ct in component_types]
        if not dicts:
            return
        smallest = min(dicts, key=lambda d: len(d))
        for eid in smallest:
            if all(eid in comps.get(ct, {}) for ct in component_types):
                yield eid
    
    _ecs_update_group = BenchmarkGroup(lambda self: self.perf_log, "5")

    @_ecs_update_group.bench("TOTAL: ECS UPDATE [CORE]")
    def update(self, camera):
        import time as _time
        # Incrementar contador de frames para caches de optimización
        self._frame_count += 1
        # Cache time.time() once per frame to avoid hundreds of syscalls
        self._frame_time = _time.time()
        
        # Reconstruir SpatialIndex sólo si ha sido invalidado
        if self._spatial_index_dirty:
            self.rebuild_spatial_index()
        
        # Ejecutar cada sistema de update (pre-built benchmarked callables)
        for _sys, fn in self._update_callables:
            fn(self, camera)
    
    _ecs_render_group = BenchmarkGroup(lambda self: self.perf_log, "4")

    @_ecs_render_group.bench("TOTAL: ECS RENDER [CORE]")
    def render(self, screen, camera):
        # Si el Graph Panel del FSM Editor está visible, no dibujar overlays del ECS
        try:
            from roguelike_editors.fsm.fsm_editor_events import get_controller
            ctrl = get_controller()
            if getattr(ctrl, 'visible', False):
                gp_ctrl = getattr(ctrl, 'graph_panel_controller', None)
                gp_model = getattr(gp_ctrl, 'model', None) if gp_ctrl else None
                if bool(getattr(gp_model, 'visible', False)):
                    try:
                        logger.debug("[ECSWorld.render] skipped ECS render because FSM Graph Panel is visible")
                    except Exception:
                        pass
                    return
        except Exception:
            pass
        # Ejecutar cada sistema de render (pre-built benchmarked callables)
        for _sys, fn in self._render_callables:
            fn(self, screen, camera)
        # Asegurar que la UI del FSM Editor quede SIEMPRE por encima de cualquier overlay del ECS
        # (barras de vida, depuración, etc.). Esto evita que elementos del juego se dibujen
        # sobre el panel del grafo o su toolbar cuando el editor está visible.
        try:
            from roguelike_editors.fsm.fsm_editor_events import FsmEditorEventHandler
            FsmEditorEventHandler.render(screen)
        except Exception:
            # Nunca romper el render principal por UI opcional
            pass

    def remove_entity(self, eid):
        self.entities.discard(eid)
        for comp_dict in self.components.values():
            # Algunos "component stores" no son dicts (p.ej., colas de eventos como listas).
            # Asegurar eliminación segura según el tipo de contenedor.
            try:
                if isinstance(comp_dict, dict):
                    comp_dict.pop(eid, None)
                elif isinstance(comp_dict, set):
                    comp_dict.discard(eid)
                else:
                    # listas/otros tipos: no están indexados por eid, ignorar
                    pass
            except Exception:
                # Nunca romper la eliminación de entidad por un componente anómalo
                pass

    def get_solid_tiles_for_rect(self, rect):
        # Delegamos totalmente al spatial_index
        return self.spatial_index.get_solid_tiles_for_rect(rect)

    def invalidate_spatial_index(self):
        """Marca SpatialIndex para reconstrucción en el próximo update."""
        self._spatial_index_dirty = True
        try:
            import os
            if os.environ.get("RL_VERBOSE_ECS") == "1":
                logger.info("[ECSWorld] invalidate_spatial_index() called -> will rebuild on next update")
        except Exception:
            pass

    def rebuild_spatial_index(self):
        """
        Reconstruye el índice espacial inmediatamente usando el `map_manager` y los
        `buildings` actuales del mundo, y limpia la bandera de suciedad.

        Preferir este método (o `invalidate_spatial_index`) desde sistemas y editores
        en lugar de asignar `world.spatial_index = SpatialIndex(...)` directamente.
        """
        # Medición básica y trazas de conteo para depuración de colliders en runtime
        try:
            if getattr(self, '_log_rebuild_info', False):
                b_count = len(self.buildings) if self.buildings is not None else 0
                b_rects = 0
                for b in (self.buildings or []):
                    try:
                        b_rects += len(b.collision_tiles)
                    except Exception:
                        pass
                map_rects = len(getattr(self.map_manager, 'solid_tiles', []) or [])
                logger.info(f"[ECSWorld] SpatialIndex rebuild: buildings={b_count} building_rects={b_rects} map_rects={map_rects}")
        except Exception:
            pass
        self.spatial_index = SpatialIndex(self.map_manager, self.buildings)
        try:
            # Tamaño del índice por celdas ocupadas (aprox broad-phase buckets)
            if getattr(self, '_log_rebuild_info', False):
                idx_cells = len(getattr(self.spatial_index, '_building_index', {}) or {})
                logger.info(f"[ECSWorld] SpatialIndex ready: building_index_cells={idx_cells}")
        except Exception:
            pass
        self._spatial_index_dirty = False
        # Reset one-shot logging flag to avoid subsequent debug/infos
        try:
            self._log_rebuild_info = False
        except Exception:
            pass

    def get_entities_in_camera(self, camera, *component_types):
        """
        Devuelve entidades dentro del área de la cámara con los componentes dados.
        """
        for eid in self.get_entities_with(*component_types):
            pos = self.components.get('Position', {}).get(eid)
            if pos is None:
                continue
            # Filtrar por área visible de la cámara usando coordenadas de pantalla
            sx, sy = camera.apply((pos.x, pos.y))
            if 0 <= sx <= camera.screen_width and 0 <= sy <= camera.screen_height:
                yield eid