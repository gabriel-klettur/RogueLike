import os
import types
from types import SimpleNamespace
import pytest
import pygame

from roguelike_engine.config.config_tiles import TILE_SIZE, INVERSE_OVERLAY_MAP
import roguelike_engine.config.config_tiles as _cfg_tiles
from roguelike_engine.map.model.layer import Layer
from roguelike_editors.map.events import async_tools
from roguelike_editors.map.services.overlay_service import set_overlay_cell, merge_zone_to_world
from roguelike_editors.map.map_tool_bar_panel.paint_tiles.paint_tiles_controller import PaintTilesController
import roguelike_engine.map.model.overlay.overlay_manager as overlay_manager
from roguelike_engine.config.map_config import global_map_settings


@pytest.fixture(scope="module", autouse=True)
def _init_pygame():
    # Initialize pygame once for Surface/transform operations
    pygame.init()
    yield
    pygame.quit()


class InMemoryOverlayStore:
    def __init__(self):
        self.data = {}

    def load(self, map_name: str):
        return self.data.get(map_name)

    def save(self, map_name: str, payload):
        self.data[map_name] = payload


class DummyView:
    def __init__(self):
        self.calls = []

    def update_chunks(self, map_model, camera, cells):
        # Record invocations for assertions
        self.calls.append(("update", list(cells)))

    def update_cells_all_zooms(self, map_model, cells):
        self.calls.append(("all_zooms", list(cells)))

    def invalidate_cache(self):
        self.calls.append(("invalidate", []))


def make_tile(char: str, row: int, col: int) -> SimpleNamespace:
    return SimpleNamespace(
        x=col * TILE_SIZE,
        y=row * TILE_SIZE,
        tile_type=char,
        overlay_code="",
        sprite=None,
        scaled_cache={},
    )


def make_fake_map_manager(w: int = 64, h: int = 64) -> SimpleNamespace:
    # Matrix of base chars
    matrix = ["." * w for _ in range(h)]
    # World overlay grid (Layer.Ground)
    world = [["" for _ in range(w)] for _ in range(h)]
    # Tiles grid for Ground layer
    tiles_ground = [[make_tile(matrix[r][c], r, c) for c in range(w)] for r in range(h)]
    tiles_by_layer = {Layer.Ground: tiles_ground}
    layers = {Layer.Ground: world}
    view = DummyView()
    return SimpleNamespace(matrix=matrix, layers=layers, tiles_by_layer=tiles_by_layer, view=view)


def make_editor_state(tile_code: str = "floor_3") -> SimpleNamespace:
    return SimpleNamespace(
        tile_code=tile_code,
        execution_list=[],
        execution_index=0,
        execution_total=0,
        dirty_cells=set(),
        current_command=None,
        undo_stack=[],
        redo_stack=[],
        recent_overlays=[],
        overlay_locks={},
        # Fields used by _handle_paint_tiles_execution()
        executing_zone="zone_test",
        last_progress_report=0,
        execution_start_time=0,
    )


def test_choice_to_overlay_code_dynamic_mapping():
    # Given a non-mapped asset name, controller should create a dynamic overlay code
    name = "unit_test_floor_variant"
    # Ensure it's not mapped initially
    INVERSE_OVERLAY_MAP.pop(name, None)
    for k in list(_cfg_tiles.OVERLAY_CODE_MAP.keys()):
        if _cfg_tiles.OVERLAY_CODE_MAP[k] == name:
            _cfg_tiles.OVERLAY_CODE_MAP.pop(k, None)
    code = PaintTilesController._choice_to_overlay_code(f"tiles/{name}.png")
    assert code is not None and isinstance(code, str)
    assert _cfg_tiles.OVERLAY_CODE_MAP.get(code) == name
    assert code in INVERSE_OVERLAY_MAP.get(name, [])


def test_apply_tile_and_world_overlay_updates_memory_and_marks_dirty():
    mm = make_fake_map_manager(8, 8)
    st = make_editor_state("floor_3")
    # pick a tile 3,5
    t = make_tile(".", 3, 5)
    st.execution_list = [t]
    st.execution_total = 1
    # Apply overlay modifications (tile + world)
    async_tools._apply_tile_overlay(t, st)
    assert t.overlay_code == "floor_3"
    async_tools._apply_ground_overlay(t, st, mm)
    tx, ty = t.x // TILE_SIZE, t.y // TILE_SIZE
    assert mm.layers[Layer.Ground][ty][tx] == "floor_3"
    assert (ty, tx) in st.dirty_cells


def test_batch_flush_triggers_update_chunks(monkeypatch):
    mm = make_fake_map_manager(16, 16)
    st = make_editor_state("floor_3")
    cam = SimpleNamespace(zoom=1.0)
    # Prepare a tile and pre-populate dirty_cells >= batch size to force flush
    t = make_tile(".", 1, 1)
    st.execution_list = [t]
    st.execution_total = 1
    batch = max(1, __import__("roguelike_engine.config.config_editor", fromlist=["TILE_PAINT_BATCH"]).TILE_PAINT_BATCH)
    # Fill dirty_cells minus one (apply will add one more)
    for i in range(batch - 1):
        st.dirty_cells.add((i % 8, i % 8))
    prev_calls = len(mm.view.calls)
    async_tools._handle_paint_tiles_execution(cam, st, SimpleNamespace(zones=None), mm)
    # Expect at least one update call due to batch flush
    assert any(tag == "update" for tag, _ in mm.view.calls[prev_calls:])


def test_finalize_paints_persist_and_merge(monkeypatch):
    # Use in-memory overlay store
    store = InMemoryOverlayStore()
    overlay_manager.set_overlay_store(store)

    # Configure zone offsets to (0,0) for a test zone
    zone = "zone_0_0"
    # Force offsets dict directly (cached_property-friendly)
    global_map_settings.__dict__["zone_offsets"] = {zone: (0, 0)}
    # Set zone size via fields (zone_size is a property)
    wz, hz = 8, 8
    global_map_settings.zone_width = wz
    global_map_settings.zone_height = hz

    # Build map manager and tiles_by_zone with overlay applied
    mm = make_fake_map_manager(wz, hz)
    tiles = []
    for r in range(hz):
        for c in range(wz):
            tile = make_tile(".", r, c)
            tile.overlay_code = "floor_3" if (r + c) % 3 == 0 else ""
            tiles.append(tile)
    mm.tiles_by_zone = {zone: tiles}

    # Editor state
    st = make_editor_state("floor_3")

    # Controller with ZonesService wired to overlay_manager
    from roguelike_engine.zone.zone_controller import ZonesService
    controller = SimpleNamespace(zones=ZonesService())

    # Intercept persistence to capture payload deterministically
    captured = {}
    def _fake_save_layers(map_name, layers):
        captured["payload"] = overlay_manager.serialize_layers_payload(layers)
    monkeypatch.setattr(overlay_manager, "save_layers", _fake_save_layers)

    # Finalize and persist
    async_tools._finalize_paint_tiles(zone, st, controller, mm)

    # Verify captured payload content (equivalent to what save_layers would write)
    payload = captured.get("payload")
    assert isinstance(payload, dict) and "layers" in payload, "No overlay payload captured"
    grid = payload["layers"].get("Ground")
    assert isinstance(grid, list) and grid and isinstance(grid[0], list)
    # Grid size may be the engine default (50x50). Ensure it's at least as big as our painted area (8x8)
    assert len(grid) >= hz and len(grid[0]) >= wz
    # Validate pattern within the painted 8x8 region
    expected_painted = 0
    for r in range(hz):
        for c in range(wz):
            want = (r + c) % 3 == 0
            val = grid[r][c]
            if want:
                expected_painted += 1
                assert val == "floor_3"
            else:
                assert val in ("", None)

    # Verify merge to world applied only non-empty codes
    world = mm.layers[Layer.Ground]
    # Painted count within the zone grid (may include zeros outside 8x8)
    painted = sum(1 for r in range(len(grid)) for c in range(len(grid[0])) if grid[r][c])
    # Merge to world should reflect exactly the expected_painted cells at their positions
    applied = sum(1 for r in range(hz) for c in range(wz) if world[r][c])
    assert applied == expected_painted
