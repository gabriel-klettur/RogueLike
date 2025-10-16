import types
import pytest

from roguelike_editors.tiles.tiles_collision_panel.tiles_collision_panel_controller import (
    TilesCollisionPanelController,
)


class DummyTile:
    def __init__(self):
        self.solid = False


class DummyGameMap:
    def __init__(self, rows=2, cols=2):
        self.tiles = [[DummyTile() for _ in range(cols)] for _ in range(rows)]
        self.matrix = [["." for _ in range(cols)] for _ in range(rows)]
        self.solid_tiles = []
        self.collision_layers = {"zone": [["." for _ in range(cols)] for _ in range(rows)]}
        self._last_update = None
        self.view = types.SimpleNamespace(update_chunks=self._upd)
    def _upd(self, m, c, cells):
        self._last_update = (m, c, cells)
    def get_zone_for(self, r, c):
        return ("zone", 0, 0)


class DummyState:
    def __init__(self):
        ts = types.SimpleNamespace()
        ts.show_collisions = True
        ts.show_collisions_overlay = False
        ts.collision_choice = '#'
        self.toolbar_state = ts
        self.size_panel_state = types.SimpleNamespace(selected_size=(1, 1))


class DummyECSWorld:
    def __init__(self):
        self.rebuilds = 0
        self.invalidations = 0
    def rebuild_spatial_index(self):
        self.rebuilds += 1
    def invalidate_spatial_index(self):
        self.invalidations += 1


class DummyEditorController:
    def __init__(self, state, tile, pos=(0, 0)):
        self.editor = state
        self._tile = tile
        self._pos = pos
        self._pending_collision_zones = set()
        self.ecs_world = DummyECSWorld()
    def _get_brush_cell(self, mouse_pos, camera, game_map):
        return (self._tile, self._pos[0], self._pos[1])


def test_apply_brush_updates_data_but_not_spatial_index():
    state = DummyState()
    game_map = DummyGameMap(2, 2)
    tile = game_map.tiles[0][1]
    editor_ctrl = DummyEditorController(state, tile, (0, 1))
    controller = TilesCollisionPanelController(editor_ctrl, types.SimpleNamespace())

    controller.apply_brush((0, 0), 'cam', game_map)

    # Data updated
    assert tile.solid is True
    assert game_map.matrix[0][1] == '#'
    assert tile in game_map.solid_tiles
    assert 'zone' in editor_ctrl._pending_collision_zones
    assert game_map._last_update[2] == [(0, 1)]

    # No spatial index ops during brush step (flush will handle it)
    assert editor_ctrl.ecs_world.rebuilds == 0
    assert editor_ctrl.ecs_world.invalidations == 0
