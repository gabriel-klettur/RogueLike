import types

import pygame
import pytest

from roguelike_game.managers.core.update_manager import update_game


class DummyState:
    running = True


class DummyCamera:
    pass


class DummyECSWorld:
    def __init__(self):
        self.calls = 0

    def update(self, camera):
        self.calls += 1

    def rebuild_spatial_index(self):
        # no-op stub used by update_manager before running the benchmarked ECS update
        pass


class DummyECS:
    def __init__(self):
        self.ecs_world = DummyECSWorld()


class DummyEditorState:
    def __init__(self, active: bool):
        self.active = active
        # Buildings Editor specific flags
        self.colliders_dirty = False
        self.last_colliders_rebuild_ms = 0
        self.colliders_rebuild_interval_ms = 120


class DummyTilesEditor:
    def __init__(self, active=False):
        self.editor_state = DummyEditorState(active)
        self.calls = 0

    def update(self, camera, game_map):
        self.calls += 1


class DummyBuildingsEditor:
    def __init__(self, active=True):
        self.editor_state = DummyEditorState(active)

    def update(self, camera):
        pass


class DummyMapEditor:
    def __init__(self, active=False):
        self.editor_state = DummyEditorState(active)

    def update(self, camera, game_map):
        pass


@pytest.mark.parametrize("dirty, expected_calls", [
    (False, 0),  # no ECS update if not dirty
    (True, 1),   # one ECS update when dirty and interval elapsed
])
def test_buildings_editor_ecs_update_gated_by_dirty(monkeypatch, dirty, expected_calls):
    state = DummyState()
    camera = DummyCamera()
    clock = object()
    screen = object()
    game_map = object()
    buildings = types.SimpleNamespace(update=lambda *a, **k: None)
    tiles_editor = DummyTilesEditor(active=False)
    buildings_editor = DummyBuildingsEditor(active=True)
    map_editor = DummyMapEditor(active=False)
    minimap = types.SimpleNamespace(update=lambda *a, **k: None)
    ecs = DummyECS()
    perf_log = {}

    # Set dirty flag and simulate that interval already elapsed
    buildings_editor.editor_state.colliders_dirty = dirty
    buildings_editor.editor_state.last_colliders_rebuild_ms = 0
    buildings_editor.editor_state.colliders_rebuild_interval_ms = 1

    # Make time due
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

    # When dirty, the benchmark key must exist and ECS world updated once
    if dirty:
        assert any(k.startswith("2.2.ecs.update[while_buildings_editor]") for k in perf_log), (
            "Benchmark for while_buildings_editor must be present when dirty"
        )
    else:
        assert not any(k.startswith("2.2.ecs.update[while_buildings_editor]") for k in perf_log), (
            "Benchmark must not run when not dirty"
        )
    assert ecs.ecs_world.calls == expected_calls
