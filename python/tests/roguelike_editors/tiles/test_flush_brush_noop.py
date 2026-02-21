import types
import pygame
import pytest

class DummyMap:
    def __init__(self):
        self._saved = 0
        self.collision_manager = types.SimpleNamespace(save=lambda zone: None, load=lambda m: {})
        self.view = types.SimpleNamespace(update_chunks=lambda *a, **k: None, invalidate_cache=lambda: None)
        self.collision_layers = {}
        self.tiles = []
        self.layers = {}
    def save_cache(self):
        self._saved += 1

class DummyCamera:
    def __init__(self):
        self.zoom = 1.0

@pytest.fixture
def controller():
    # Import class but avoid calling __init__ (which creates views needing display)
    from roguelike_editors.tiles.tile_editor_controller import TileEditorController
    ctrl = object.__new__(TileEditorController)
    # Initialize only what flush_brush uses
    ctrl._pending_collision_zones = set()
    ctrl._pending_tile_zones = set()
    ctrl._pending_cells = []
    ctrl._pending_cells_set = set()
    ctrl._last_brush_cell = None
    ctrl._did_partial_updates = False
    ctrl._last_chunk_update_ms = 0
    return ctrl


def test_flush_brush_does_not_save_when_no_changes(controller):
    m = DummyMap()
    cam = DummyCamera()

    # Ensure there are no pending changes
    controller._pending_collision_zones.clear()
    controller._pending_tile_zones.clear()
    controller._pending_cells.clear()

    controller.flush_brush(m, cam)

    assert m._saved == 0, "flush_brush should not save cache when there are no changes"


def test_flush_brush_saves_when_collision_changes(controller):
    m = DummyMap()
    cam = DummyCamera()

    controller._pending_collision_zones = {"Z1"}
    controller._pending_tile_zones = set()
    controller._pending_cells = []

    controller.flush_brush(m, cam)

    assert m._saved == 1, "flush_brush should save cache when there are collision changes"
