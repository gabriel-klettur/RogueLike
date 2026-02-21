import types

import pytest

from roguelike_game.managers.core.update_manager import update_game


class DummyState:
    running = True


class DummyCamera:
    pass


class DummyECSWorld:
    def __init__(self):
        self.calls = 0
        # minimal attributes used by update_manager camera/minimap steps
        self.player_entity = -1
        self.components = {}
    def update(self, camera):
        self.calls += 1


class DummyECS:
    def __init__(self):
        self.ecs_world = DummyECSWorld()


class DummyEditorState:
    def __init__(self, active: bool):
        self.active = active


class DummyTilesEditor:
    def __init__(self, active=True):
        self.editor_state = DummyEditorState(active)
        self.calls = 0
    def update(self, camera, game_map):
        self.calls += 1


class DummyBuildingsEditor:
    def __init__(self, active=False):
        self.editor_state = DummyEditorState(active)
    def update(self, camera):
        pass


class DummyMapEditor:
    def __init__(self, active=False):
        self.editor_state = DummyEditorState(active)
    def update(self, camera, game_map):
        pass


@pytest.mark.parametrize("tiles_active", [True, False])
def test_update_game_runs_ecs_update_when_tiles_editor_active(tiles_active):
    state = DummyState()
    camera = DummyCamera()
    clock = object()
    screen = object()
    game_map = object()
    buildings = types.SimpleNamespace(update=lambda *a, **k: None)
    tiles_editor = DummyTilesEditor(active=tiles_active)
    buildings_editor = DummyBuildingsEditor(active=False)
    map_editor = DummyMapEditor(active=False)
    minimap = types.SimpleNamespace(update=lambda *a, **k: None)
    ecs = DummyECS()
    perf_log = {}

    update_game(
        state,
        camera,
        clock,
        screen,
        game_map,
        buildings,
        tiles_editor,
        buildings_editor,
        map_editor,
        minimap,
        ecs,
        perf_log,
    )

    if tiles_active:
        assert ecs.ecs_world.calls == 1, "ECS update must run when tiles editor is active"
        assert tiles_editor.calls == 1, "Tiles editor should run once"
    else:
        assert ecs.ecs_world.calls == 0, "ECS update should not be forced when editor inactive"
