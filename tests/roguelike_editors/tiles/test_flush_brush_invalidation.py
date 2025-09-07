import types
import pytest


class DummyMap:
    def __init__(self):
        self._saved = 0
        self.collision_manager = types.SimpleNamespace(save=lambda zone: None, load=lambda m: {})
        self.view = types.SimpleNamespace(
            update_chunks=lambda *a, **k: None,
            invalidate_cache=lambda: self._mark_inv()
        )
        self._invalidations = 0
        self.collision_layers = {}
        self.tiles = [[object()]]
        self.layers = {}
    def save_cache(self):
        self._saved += 1
    def _mark_inv(self):
        self._invalidations += 1


class DummyCamera:
    def __init__(self):
        self.zoom = 1.0


@pytest.fixture
def controller():
    from roguelike_editors.tiles.tile_editor_controller import TileEditorController
    ctrl = object.__new__(TileEditorController)
    # Minimal init fields used by flush_brush
    ctrl._pending_collision_zones = set()
    ctrl._pending_tile_zones = set()
    ctrl._pending_cells = []
    ctrl._pending_cells_set = set()
    ctrl._last_brush_cell = None
    ctrl._did_partial_updates = False
    ctrl._last_chunk_update_ms = 0
    return ctrl


def test_flush_brush_forces_invalidation_when_cells_changed(controller):
    m = DummyMap()
    cam = DummyCamera()

    # Simulate some changed cells
    controller._pending_collision_zones = set()
    controller._pending_tile_zones = {"Z1"}
    controller._pending_cells = [(0, 0)]

    controller.flush_brush(m, cam)

    assert m._invalidations >= 1, "flush_brush should force view.invalidate_cache to ensure on-screen refresh"
    assert m._saved == 1, "flush_brush should save cache when there are changes"


def test_flush_brush_invalidation_even_with_partial_updates(controller, monkeypatch):
    m = DummyMap()
    cam = DummyCamera()

    controller._did_partial_updates = True
    controller._pending_collision_zones = set()
    controller._pending_tile_zones = {"Z2"}
    controller._pending_cells = [(0, 0)]

    # Even if partial updates happened, final invalidate should still occur
    controller.flush_brush(m, cam)
    assert m._invalidations >= 1
