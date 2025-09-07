from .component_registry import create_empty_component_store
from .system_registry import get_update_system_classes, get_render_system_classes
from .spatial_index import SpatialIndex
from roguelike_engine.utils.benchmark import benchmark
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

        self.entities = []
        self.next_entity_id = 1

        # 1) Inicializar índice espacial
        self.spatial_index = SpatialIndex(map_manager, buildings)
        # Flag para evitar reconstruir SpatialIndex cada frame
        self._spatial_index_dirty = False

        # 2) Registrar componentes
        self.components = create_empty_component_store()

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

    def create_entity(self):
        eid = self.next_entity_id
        self.next_entity_id += 1
        self.entities.append(eid)
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
    
    def update(self, camera):
        # Reconstruir SpatialIndex sólo si ha sido invalidado
        if self._spatial_index_dirty:
            self.spatial_index = SpatialIndex(self.map_manager, self.buildings)
            self._spatial_index_dirty = False
        
        # Ejecutar cada sistema de update
        for i, system in enumerate(self.update_systems, start=1):
            name = type(system).__name__
            @benchmark(self.perf_log, f"5.{i:02d}.[UPDATE]{name}")
            def _update_sys(sys=system):
                sys.update(self, camera)
            _update_sys()
    
    def render(self, screen, camera):
        # Si el Graph Panel del FSM Editor está visible, no dibujar overlays del ECS
        # (barras de vida, debug, etc.) para que no se vean por encima del panel.
        # El mundo base ya se dibuja en RendererManager antes de la fase ECS.
        try:
            from roguelike_editors.fsm.fsm_editor_events import get_controller
            ctrl = get_controller()
            if getattr(ctrl, 'visible', False):
                gp_ctrl = getattr(ctrl, 'graph_panel_controller', None)
                gp_model = getattr(gp_ctrl, 'model', None) if gp_ctrl else None
                if bool(getattr(gp_model, 'visible', False)):
                    return
        except Exception:
            pass
        # Ejecutar cada sistema de render
        for i, system in enumerate(self.render_systems, start=1):
            name = type(system).__name__
            @benchmark(self.perf_log, f"4.{i:02d}.[RENDER]{name}")
            def _render_sys(sys=system):
                sys.update(self, screen, camera)
            _render_sys()
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
        if eid in self.entities:
            self.entities.remove(eid)
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