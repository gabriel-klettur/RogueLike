import pytest
from types import SimpleNamespace
import roguelike_editors.tiles.tile_editor_controller as module
from roguelike_editors.tiles.tile_editor_controller import TileEditorController
from roguelike_editors.tiles.tile_editor_state import TileEditorState

@pytest.fixture
def controller(monkeypatch):
    # Monkeypatch dependent controllers and views
    class Dummy:
        def __init__(self, *args, **kwargs):
            self.args = args
    targets = [
        'TilePickerController', 'TileToolbarController', 'TilesViewPanelController',
        'TilesTitleController', 'TilesCollisionPanelController', 'LayersPanelController',
        'SizePanelController', 'TileOutlineView'
    ]
    for name in targets:
        monkeypatch.setattr(
            'roguelike_editors.tiles.tile_editor_controller.' + name,
            Dummy
        )
    state = TileEditorState()
    ctrl = TileEditorController(state, state.picker_state)
    return ctrl, state


def test_init(controller):
    ctrl, state = controller
    # Picker
    assert isinstance(ctrl.picker, module.TilePickerController)
    assert ctrl.picker.args == (ctrl, state, state.picker_state)
    # Toolbar
    assert isinstance(ctrl.toolbar, module.TileToolbarController)
    assert ctrl.toolbar.args == (ctrl,)
    # View panel
    assert isinstance(ctrl.view_panel_controller, module.TilesViewPanelController)
    assert ctrl.view_panel_controller.args == (ctrl, state.view_panel_state)
    # Title
    assert isinstance(ctrl.title_controller, module.TilesTitleController)
    assert ctrl.title_controller.args == (state, state.title_state)
    # Collision panel
    assert isinstance(ctrl.collision_panel_controller, module.TilesCollisionPanelController)
    assert ctrl.collision_panel_controller.args == (ctrl, state.collision_panel_state)
    # Layers panel
    assert isinstance(ctrl.layers_panel_controller, module.LayersPanelController)
    assert ctrl.layers_panel_controller.args == (ctrl, state.layers_panel_state)
    # Size panel
    assert isinstance(ctrl.size_panel_controller, module.SizePanelController)
    assert ctrl.size_panel_controller.args == (ctrl, state.size_panel_state)
    # Outline
    assert isinstance(ctrl.outline_view, module.TileOutlineView)
    assert ctrl.outline_view.args == (ctrl, state)


def test_select_tile_at(controller):
    ctrl, state = controller
    ctrl._tile_under_mouse = lambda mp, cam, m: 'tile'
    ctrl.select_tile_at((0, 0), None, None)
    assert state.selected_tile == 'tile'


def test_tile_under_mouse(monkeypatch, controller):
    ctrl, state = controller
    # Prepare map with one tile
    tile = object()
    game_map = SimpleNamespace(tiles=[[tile]], view=None)
    # Monkeypatch screen_to_tile
    monkeypatch.setattr(
        'roguelike_editors.tiles.tile_editor_controller.screen_to_tile',
        lambda mp, cam: (0, 0)
    )
    res = ctrl._tile_under_mouse((0, 0), None, game_map)
    assert res is tile
    # Out of bounds
    game_map.tiles = [[]]
    res2 = ctrl._tile_under_mouse((0, 0), None, game_map)
    assert res2 is None


def test_get_brush_cell(controller):
    ctrl, state = controller
    class Tile:
        def __init__(self, x, y):
            self.x = x
            self.y = y
            self.scaled_cache = []
    t = Tile(32, 32)
    game_map = SimpleNamespace(tiles=[[t]])
    ctrl._last_brush_cell = None
    # Provide dummy camera to avoid screen_to_tile dependency
    camera = SimpleNamespace(offset_x=0, offset_y=0, zoom=1)
    tile, row, col = ctrl._get_brush_cell((0, 0), camera, game_map)
    assert tile is t and row == 1 and col == 1
    # Duplicate
    tile2, r2, c2 = ctrl._get_brush_cell((0, 0), camera, game_map)
    assert tile2 is None and r2 is None and c2 is None


def test_start_brush(controller):
    ctrl, state = controller
    # Setup pending
    ctrl._pending_collision_zones.add('A')
    ctrl._pending_tile_zones.add('B')
    ctrl._pending_cells.append((1, 1))
    ctrl._last_brush_cell = (1, 1)
    ctrl.start_brush()
    assert ctrl._pending_collision_zones == set()
    assert ctrl._pending_tile_zones == set()
    assert ctrl._pending_cells == []
    assert ctrl._last_brush_cell is None
