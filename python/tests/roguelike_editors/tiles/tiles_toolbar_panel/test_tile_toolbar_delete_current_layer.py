import pygame
import pytest
from types import SimpleNamespace

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


def _mk_map(width=5, height=5, layers=("Ground", "Objects")):
    tiles = [[_Tile(r, c, tile_type="#", overlay="") for c in range(width)] for r in range(height)]
    tiles_by_layer = {}
    layers_codes = {}
    layer_char = {"Ground": "G", "Objects": "O"}
    for lname in layers:
        grid = [[_Tile(r, c, tile_type=layer_char.get(lname, "#"), overlay="Y") for c in range(width)] for r in range(height)]
        tiles_by_layer[lname] = grid
        layers_codes[lname] = [["Y" for _ in range(width)] for _ in range(height)]
    view = SimpleNamespace(
        update_chunks=lambda *a, **k: None,
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
    return game_map


@pytest.fixture
def controller(monkeypatch):
    # Avoid icon I/O
    monkeypatch.setattr(TileToolbarController, "_load_icons", lambda self: {})
    # Build editor controller/state
    size_state = SimpleNamespace(selected_size=(2, 2))
    toolbar_state = SimpleNamespace(view_active=False)
    editor_state = SimpleNamespace(
        toolbar_state=toolbar_state,
        size_panel_state=size_state,
        selected_tile=None,
        current_layer="Ground",
        current_tool="select",
    )
    editor_controller = SimpleNamespace(editor=editor_state)
    # Pending batching structures
    editor_controller._pending_tile_zones = set()
    editor_controller._pending_cells = []
    editor_controller._pending_cells_set = set()
    ctrl = TileToolbarController(editor_controller)
    return ctrl, editor_state


def test_delete_affects_only_current_layer(controller):
    ctrl, editor_state = controller
    game_map = _mk_map(width=4, height=4)

    origin_r, origin_c = 1, 1
    editor_state.selected_tile = game_map.tiles_by_layer["Ground"][origin_r][origin_c]

    # Before: overlays populated with 'Y' in both layers within 2x2 region
    for lname in ("Ground", "Objects"):
        for dy in range(2):
            for dx in range(2):
                r, c = origin_r + dy, origin_c + dx
                assert game_map.layers[lname][r][c] == "Y"
                assert game_map.tiles_by_layer[lname][r][c].overlay_code == "Y"

    # Delete on Ground layer only
    ctrl.delete_tile(game_map, camera=None)

    # Ground should be cleared
    for dy in range(2):
        for dx in range(2):
            r, c = origin_r + dy, origin_c + dx
            t = game_map.tiles_by_layer["Ground"][r][c]
            assert t.overlay_code == ""
            assert game_map.layers["Ground"][r][c] == ""
            # Transparent surface expected
            assert isinstance(t.sprite, pygame.Surface)
            assert t.sprite.get_alpha() in (None,)

    # Objects must remain untouched
    for dy in range(2):
        for dx in range(2):
            r, c = origin_r + dy, origin_c + dx
            t = game_map.tiles_by_layer["Objects"][r][c]
            assert t.overlay_code == "Y"
            assert game_map.layers["Objects"][r][c] == "Y"
