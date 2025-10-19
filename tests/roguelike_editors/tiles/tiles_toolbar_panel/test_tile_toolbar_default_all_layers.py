import pygame
import pytest
from types import SimpleNamespace

from roguelike_editors.tiles.tiles_toolbar_panel import tile_toolbar_controller as ttc
from roguelike_editors.tiles.tiles_toolbar_panel.tile_toolbar_controller import TileToolbarController
from roguelike_engine.config.config_tiles import TILE_SIZE


class _Tile:
    def __init__(self, r: int, c: int, tile_type: str, overlay: str = "ov"):
        self.x = c * TILE_SIZE
        self.y = r * TILE_SIZE
        self.tile_type = tile_type
        self.overlay_code = overlay
        self.sprite = pygame.Surface((TILE_SIZE, TILE_SIZE))
        self.scaled_cache = {}


def _mk_map(width=5, height=5, layers=("Ground", "Objects", "Upper")):
    tiles = [[_Tile(r, c, tile_type="#", overlay="") for c in range(width)] for r in range(height)]
    tiles_by_layer = {}
    layers_codes = {}
    # Use distinct tile_type per layer to verify per-layer base mapping
    layer_char = {"Ground": "G", "Objects": "O", "Upper": "U"}
    for lname in layers:
        grid = [[_Tile(r, c, tile_type=layer_char.get(lname, "#"), overlay="") for c in range(width)] for r in range(height)]
        tiles_by_layer[lname] = grid
        layers_codes[lname] = [["" for _ in range(width)] for _ in range(height)]
    view_calls = []
    view = SimpleNamespace(
        update_chunks=lambda game_map, camera, cells: view_calls.append((tuple(sorted(set(cells))), camera)),
        invalidate_cache=lambda: None,
    )

    def get_zone_for(r, c):
        return ("zoneA", 0, 0)

    game_map = SimpleNamespace(
        tiles=tiles,
        tiles_by_layer=tiles_by_layer,
        layers=layers_codes,
        view=view,
        get_zone_for=get_zone_for,
    )
    return game_map, view_calls


@pytest.fixture
def controller(monkeypatch):
    # Avoid icon I/O
    monkeypatch.setattr(TileToolbarController, "_load_icons", lambda self: {})
    # Patch asset loaders used by controller
    surf_g = pygame.Surface((3, 3))
    surf_o = pygame.Surface((3, 3))
    surf_u = pygame.Surface((3, 3))

    def fake_load_base_tile_images():
        # Map tile_type -> base surface
        return {"G": surf_g, "O": surf_o, "U": surf_u, "#": pygame.Surface((3, 3))}

    monkeypatch.setattr(ttc, "load_base_tile_images", fake_load_base_tile_images, raising=True)
    monkeypatch.setattr(ttc, "load_image", lambda path, size: pygame.Surface(size), raising=True)
    # Disable throttle to ensure chunk updates fire deterministically
    monkeypatch.setattr(ttc, "BRUSH_UPDATE_THROTTLE_MS", 0, raising=False)

    # Build editor controller/state
    size_state = SimpleNamespace(selected_size=(2, 2))
    toolbar_state = SimpleNamespace(view_active=False, default_applied_since_activation=False)
    editor_state = SimpleNamespace(
        toolbar_state=toolbar_state,
        size_panel_state=size_state,
        selected_tile=None,
        current_layer="Ground",
        current_tool="select",
    )
    editor_controller = SimpleNamespace(editor=editor_state)
    ctrl = TileToolbarController(editor_controller)
    # Ensure throttle allows update
    setattr(editor_controller, "_last_chunk_update_ms", 0)
    # Pending structures required by controller during batched ops
    editor_controller._pending_tile_zones = set()
    editor_controller._pending_cells = []
    editor_controller._pending_cells_set = set()
    return ctrl, editor_state


def test_default_resets_all_layers(controller):
    ctrl, editor_state = controller
    game_map, view_calls = _mk_map(width=4, height=4)

    # Seed overlays and sprites in target region across all layers to non-default values
    origin_r, origin_c = 1, 1
    w, h = editor_state.size_panel_state.selected_size
    for lname, grid in game_map.tiles_by_layer.items():
        for dy in range(h):
            for dx in range(w):
                r, c = origin_r + dy, origin_c + dx
                t = grid[r][c]
                t.overlay_code = "X"
                game_map.layers[lname][r][c] = "X"
                t.sprite = pygame.Surface((TILE_SIZE, TILE_SIZE))  # non-base placeholder
                t.scaled_cache = {"dummy": True}

    # Select the anchor tile at origin on Ground layer
    editor_state.selected_tile = game_map.tiles_by_layer["Ground"][origin_r][origin_c]

    # Apply default (should affect all layers now)
    ctrl.set_default(game_map, camera=None)

    # Verify overlays cleared and sprites set to base for ALL layers
    for lname, grid in game_map.tiles_by_layer.items():
        for dy in range(h):
            for dx in range(w):
                r, c = origin_r + dy, origin_c + dx
                t = grid[r][c]
                assert t.overlay_code == ""
                assert game_map.layers[lname][r][c] == ""
                # Base surface is 3x3 per fake loader; ensure cache cleared
                assert isinstance(t.sprite, pygame.Surface)
                assert t.sprite.get_size() in [(3, 3), (TILE_SIZE, TILE_SIZE)]
                assert t.scaled_cache == {}

    # update_chunks should have been called with deduped cells equal to region area
    assert len(view_calls) >= 1
    last_cells, _ = view_calls[-1]
    assert len(last_cells) == w * h
