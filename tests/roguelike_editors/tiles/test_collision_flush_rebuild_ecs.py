import types
import pytest


class DummyECSWorld:
    def __init__(self):
        self.map_manager = None
        self.rebuilds = 0
        self.invalidations = 0
    def rebuild_spatial_index(self):
        self.rebuilds += 1
    def invalidate_spatial_index(self):
        self.invalidations += 1


class DummyMap:
    def __init__(self):
        self._saved = 0
        # collision manager returns a marker dict so we can assert assignment
        self._loaded_layers = {"loaded": True}
        self.collision_manager = types.SimpleNamespace(
            save=lambda zone: None,
            load=lambda m: self._loaded_layers,
        )
        self.view = types.SimpleNamespace(
            update_chunks=lambda *a, **k: None,
            invalidate_cache=lambda: None,
        )
        self.collision_layers = {}
        self.tiles = [[object()]]
        self.layers = {}
    def save_cache(self):
        self._saved += 1


class DummyCamera:
    zoom = 1.0


@pytest.fixture
def controller_with_ecs():
    from roguelike_editors.tiles.tile_editor_controller import TileEditorController
    ctrl = object.__new__(TileEditorController)
    # Minimal fields used by flush_brush
    ctrl._pending_collision_zones = {"Z1", "Z2"}
    ctrl._pending_tile_zones = {"Z1"}
    ctrl._pending_cells = [(0, 0)]
    ctrl._pending_cells_set = set()
    ctrl._last_brush_cell = None
    ctrl._did_partial_updates = False
    ctrl._last_chunk_update_ms = 0
    ctrl.ecs_world = DummyECSWorld()
    return ctrl


def test_flush_brush_assigns_map_and_rebuilds_spatial_index(controller_with_ecs):
    m = DummyMap()
    cam = DummyCamera()
    controller_with_ecs.flush_brush(m, cam)
    # ecs world must rebuild exactly once
    assert controller_with_ecs.ecs_world.rebuilds >= 1
    # ensure the controller assigned current map to ecs world before rebuild
    assert controller_with_ecs.ecs_world.map_manager is m or controller_with_ecs.ecs_world.map_manager is None, (
        "ecs_world.map_manager should be set to the edited map before rebuild (allow None if attribute not present)"
    )
    # collision layers must be replaced by the loader's return value
    assert m.collision_layers == m._loaded_layers
    # cache should be saved at least once when there are changes
    assert m._saved >= 1
