# Path: src/roguelike_game/ecs/core/manager.py
from .component_registry import create_empty_component_store
from .system_registry import get_update_system_classes, get_render_system_classes
from .spatial_index import SpatialIndex
from roguelike_engine.utils.benchmark import benchmark
import os
from roguelike_game.ecs.systems.input.input_system import InputSystem
from roguelike_game.ecs.systems.inventory.inventory_pickup_system import InventoryPickupSystem
from .spawn_manager import SpawnNPCManager

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

        # 4) Crear spawn inicial
        self.spawn_npc_manager = SpawnNPCManager(self)
        # spawn_npc_initial se llamará después de crear el jugador en ECSManager

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
            elif cls is InventoryPickupSystem:
                inst = cls()
            else:
                inst = cls(self.perf_log)
            self.update_systems.append(inst)
        self.render_systems = [cls(self.perf_log) for cls in render_classes]
        print(f"[ECSWorld] Update systems: {[type(s).__name__ for s in self.update_systems]}")
        print(f"[ECSWorld] Render systems: {[type(s).__name__ for s in self.render_systems]}")

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
        for system in self.update_systems:
            name = type(system).__name__
            @benchmark(self.perf_log, f"4.2.[UPDATE]{name}")
            def _update_sys(sys=system):
                sys.update(self, camera)
            _update_sys()

    def render(self, screen, camera):
        # Ejecutar cada sistema de render
        for system in self.render_systems:
            name = type(system).__name__
            @benchmark(self.perf_log, f"4.2.[RENDER]{name}")
            def _render_sys(sys=system):
                sys.update(self, screen, camera)
            _render_sys()

    def remove_entity(self, eid):
        if eid in self.entities:
            self.entities.remove(eid)
        for comp_dict in self.components.values():
            comp_dict.pop(eid, None)

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