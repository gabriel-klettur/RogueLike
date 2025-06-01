# Path: src/roguelike_game/ecs/core/manager.py

from .component_registry import create_empty_component_store
from .system_registry import get_update_system_classes, get_render_system_classes
from .spatial_index import SpatialIndex
from .spawn_manager import SpawnManager

class ECSWorld:
    def __init__(self, screen, map_manager, buildings, perf_log=None):
        self.perf_log = perf_log
        self.screen = screen
        self.map_manager = map_manager
        self.buildings = buildings

        self.entities = []
        self.next_entity_id = 1

        # 1) Inicializar índice espacial
        self.spatial_index = SpatialIndex(map_manager, buildings)

        # 2) Registrar componentes
        self.components = create_empty_component_store()

        # 3) Instanciar sistemas (update + render)
        self._init_systems()

        # 4) Crear y disparar spawn inicial
        self.spawn_manager = SpawnManager(self)
        self.spawn_manager.spawn_initial()

    @property
    def player_position(self):
        return self.components['Position'].get(self.player_entity)

    def _init_systems(self):
        # Obtener las clases de sistemas
        update_classes = get_update_system_classes()
        render_classes = get_render_system_classes()

        # Instanciar cada uno, pasándole perf_log (u otros parámetros si hicieran falta)
        self.update_systems = [cls(self.perf_log) for cls in update_classes]
        self.render_systems = [cls(self.perf_log) for cls in render_classes]

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
        # Ejecutar cada sistema de update
        for system in self.update_systems:
            system.update(self, camera)

    def render(self, screen, camera):
        # Ejecutar cada sistema de render
        for system in self.render_systems:
            system.update(self, screen, camera)

    def remove_entity(self, eid):
        if eid in self.entities:
            self.entities.remove(eid)
        for comp_dict in self.components.values():
            comp_dict.pop(eid, None)

    def get_solid_tiles_for_rect(self, rect):
        # Delegamos totalmente al spatial_index
        return self.spatial_index.get_solid_tiles_for_rect(rect)
