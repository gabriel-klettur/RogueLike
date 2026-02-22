import pytest
from types import SimpleNamespace
import pygame
from roguelike_editors.tiles.tiles_collision_panel.tiles_collision_panel_controller import TilesCollisionPanelController

class DummyTile:
    def __init__(self):
        self.solid = False

class DummyView:
    def __init__(self):
        self.updated = False
        self.args = None
    def update_chunks(self, game_map, camera, cells):
        self.updated = True
        self.args = (game_map, camera, cells)

class DummyGameMap:
    def __init__(self, rows, cols):
        self.tiles = [[DummyTile() for _ in range(cols)] for _ in range(rows)]
        self.matrix = [["." for _ in range(cols)] for _ in range(rows)]
        self.solid_tiles = []
        # collision layer named 'zone'
        self.collision_layers = {"zone": [["." for _ in range(cols)] for _ in range(rows)]}
        self.view = SimpleNamespace(update_chunks=self._dummy_update)
        self._last_update = None
    def _dummy_update(self, gm, cam, cells):
        self._last_update = (gm, cam, cells)
    def get_zone_for(self, r, c):
        return ("zone", 0, 0)

class DummyEditorState:
    def __init__(self):
        ts = SimpleNamespace()
        ts.show_collisions = False
        ts.show_collisions_overlay = False
        ts.collision_choice = None
        self.toolbar_state = ts
        # size_panel_state holds brush size
        self.size_panel_state = SimpleNamespace(selected_size=(1, 1))

class DummyEditorController:
    def __init__(self, state, tile, pos=(0, 0)):
        self.editor = state
        self._tile = tile
        self._pos = pos
        self._pending_collision_zones = set()
    def _get_brush_cell(self, mouse_pos, camera, game_map):
        # Always return the target tile and pos
        return (self._tile, self._pos[0], self._pos[1])

@pytest.fixture
def setup_controller():
    # Setup state and controller
    state = DummyEditorState()
    ctrl = DummyEditorController(state, None)
    return state, ctrl

@pytest.fixture
def controller_and_panel(setup_controller):
    state, editor_ctrl = setup_controller
    panel_state = SimpleNamespace()
    controller = TilesCollisionPanelController(editor_ctrl, panel_state)
    return state, editor_ctrl, panel_state, controller

class DummyScreen:
    pass

def test_init_sets_attributes(controller_and_panel):
    state, editor_ctrl, panel_state, controller = controller_and_panel
    assert controller.editor_controller is editor_ctrl
    assert controller.editor_state is editor_ctrl.editor
    assert hasattr(controller, 'view')

def test_render_delegates_to_view(controller_and_panel):
    _, _, _, controller = controller_and_panel
    screen = DummyScreen()
    called = {}
    def fake_render(scr):
        called['screen'] = scr
    controller.view.render = fake_render
    controller.render(screen)
    assert called.get('screen') is screen

def test_apply_brush_no_action_when_disabled(controller_and_panel):
    state, editor_ctrl, panel_state, controller = controller_and_panel
    game_map = DummyGameMap(2, 2)
    # All toolbar flags False
    controller.apply_brush((0, 0), None, game_map)
    # No tiles updated
    assert game_map.solid_tiles == []

def test_apply_brush_adds_collision_and_updates(controller_and_panel):
    state, editor_ctrl, panel_state, controller = controller_and_panel
    # Enable collisions
    state.toolbar_state.show_collisions = True
    state.toolbar_state.collision_choice = '#'
    # Prepare game_map and tile
    game_map = DummyGameMap(2, 2)
    # Set target tile
    tile = game_map.tiles[0][1]
    editor_ctrl._tile = tile
    editor_ctrl._pos = (0, 1)
    # Apply brush
    controller.apply_brush((0, 0), 'cam', game_map)
    # Tile should be solid
    assert tile.solid is True
    # Matrix updated
    assert game_map.matrix[0][1] == '#'
    # Added to solid_tiles
    assert tile in game_map.solid_tiles
    # Zone added to pending
    assert 'zone' in editor_ctrl._pending_collision_zones
    # update_chunks called with correct cells
    assert game_map._last_update[2] == [(0, 1)]

def test_apply_brush_removes_collision(controller_and_panel):
    state, editor_ctrl, panel_state, controller = controller_and_panel
    # Enable collisions
    state.toolbar_state.show_collisions = True
    state.toolbar_state.collision_choice = '.'
    game_map = DummyGameMap(2, 2)
    # Prepare tile solid and in solid_tiles
    tile = game_map.tiles[1][0]
    tile.solid = True
    game_map.solid_tiles.append(tile)
    editor_ctrl._tile = tile
    editor_ctrl._pos = (1, 0)
    controller.apply_brush((0, 0), None, game_map)
    # Tile should not be solid
    assert tile.solid is False
    # Removed from solid_tiles
    assert tile not in game_map.solid_tiles
    # update_chunks called with correct cells
    assert game_map._last_update[2] == [(1, 0)]

# Note: Spatial index rebuild/invalidation happens on flush (mouse-up), not on each brush step.
# Brush step tests should verify visual updates only (update_chunks) and tile/matrix state changes.

@pytest.mark.parametrize("show, overlay, choice", [
    (False, False, '#'),
    (False, True, None),
])
def test_apply_brush_conditions_skip(show, overlay, choice, controller_and_panel):
    state, editor_ctrl, panel_state, controller = controller_and_panel
    state.toolbar_state.show_collisions = show
    state.toolbar_state.show_collisions_overlay = overlay
    state.toolbar_state.collision_choice = choice
    game_map = DummyGameMap(1, 1)
    # Ensure no exception
    controller.apply_brush((0, 0), None, game_map)
    assert game_map.solid_tiles == []
