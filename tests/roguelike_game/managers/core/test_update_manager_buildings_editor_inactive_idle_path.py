import types

import pygame

from roguelike_game.managers.core.update_manager import update_game


class DummyState:
    running = True


class DummyCamera:
    pass


class DummyECSWorld:
    def __init__(self):
        self.calls = 0
        # minimal attributes accessed by camera/minimap steps
        self.player_entity = -1
        self.components = {}

    def update(self, camera):
        self.calls += 1

    def rebuild_spatial_index(self):
        # pretend heavy work
        pass


class DummyECS:
    def __init__(self):
        self.ecs_world = DummyECSWorld()


class DummyEditorState:
    def __init__(self, active: bool):
        self.active = active
        # Buildings Editor specific flags
        self.colliders_dirty = True
        self.last_colliders_rebuild_ms = 0
        self.colliders_rebuild_interval_ms = 1


class DummyTilesEditor:
    def __init__(self, active=False):
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


def test_buildings_editor_inactive_uses_idle_after_rebuild(monkeypatch):
    state = DummyState()
    camera = DummyCamera()
    clock = object()
    screen = object()
    game_map = object()
    buildings = types.SimpleNamespace(update=lambda *a, **k: None, buildings=[])
    tiles_editor = DummyTilesEditor(active=False)
    buildings_editor = DummyBuildingsEditor(active=False)
    map_editor = DummyMapEditor(active=False)
    minimap = types.SimpleNamespace(update=lambda *a, **k: None)
    ecs = DummyECS()
    perf_log = {}

    # time due
    monkeypatch.setattr(pygame.time, "get_ticks", lambda: 10_000)

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

    assert any(k.startswith("2.2.ecs.update[after_rebuild]") for k in perf_log), (
        "When BE is inactive but dirty, the IDLE after_rebuild path should run"
    )
    assert not any(k.startswith("2.2.ecs.update[while_buildings_editor]") for k in perf_log), (
        "while_buildings_editor must not run when editor is inactive"
    )
