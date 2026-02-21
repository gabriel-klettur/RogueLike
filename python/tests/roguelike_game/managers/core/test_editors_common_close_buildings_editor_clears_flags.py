import types

import pygame

from roguelike_game.managers.core.events.handlers.editors_common import close_all_editors


class DummyEditorState:
    def __init__(self):
        self.active = True
        self.picker_active = True
        self.colliders_dirty = True
        self.last_colliders_rebuild_ms = 0


class DummyBuildingsEditor:
    def __init__(self):
        self.editor_state = DummyEditorState()
        # Provide colliders/events with _save_collisions used on close (no-op here)
        self.colliders = types.SimpleNamespace(events=types.SimpleNamespace(_save_collisions=lambda *a, **k: None))


class DummyECSWorld:
    def __init__(self):
        self.invalidated = 0

    def invalidate_spatial_index(self):
        self.invalidated += 1


class DummyGame:
    def __init__(self):
        self.buildings_editor = DummyBuildingsEditor()
        self.buildings = types.SimpleNamespace(buildings=[])
        self.state = types.SimpleNamespace(z_state=None)
        self.ecs = types.SimpleNamespace(ecs_world=DummyECSWorld())


def test_close_all_editors_clears_buildings_editor_flags(monkeypatch):
    # Avoid any file IO in save_buildings_split
    import roguelike_editors.buildings.utils.save_buildings_to_json as saver
    monkeypatch.setattr(saver, "save_buildings_split", lambda *a, **k: None, raising=False)

    game = DummyGame()

    # Ensure starting flags
    be = game.buildings_editor.editor_state
    assert be.active is True
    assert be.colliders_dirty is True

    # Run close
    close_all_editors(game)

    # Active false and colliders flags cleared
    assert game.buildings_editor.editor_state.active is False
    assert game.buildings_editor.editor_state.picker_active is False
    assert game.buildings_editor.editor_state.colliders_dirty is False
    assert isinstance(game.buildings_editor.editor_state.last_colliders_rebuild_ms, int)

    # Spatial index invalidated once
    assert game.ecs.ecs_world.invalidated == 1
