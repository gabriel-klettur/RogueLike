from types import SimpleNamespace

import pygame

import roguelike_engine.input.events as evmod


def test_wheel_event_is_not_double_processed_when_map_or_tiles_editor_active(monkeypatch):
    called = {"mouse": 0}

    def _stub_mouse(event, *a, **k):
        called["mouse"] += 1
        return True

    # Replace handle_mouse used inside events module
    monkeypatch.setattr(evmod, "handle_mouse", _stub_mouse)

    state = SimpleNamespace(running=True)
    camera = clock = menu = map_manager = entities = None

    class _Ed:
        editor_state = SimpleNamespace(active=False)
        handler = SimpleNamespace(handle=lambda *a, **k: None)

    tiles_editor = _Ed()
    buildings_editor = _Ed()
    map_editor = _Ed()

    # Activate map editor -> wheel should be skipped by events (already handled upstream)
    map_editor.editor_state.active = True
    evs = [pygame.event.Event(pygame.MOUSEWHEEL, {"y": 1})]
    evmod.handle_events(state, camera, clock, menu, map_manager, entities, tiles_editor, buildings_editor, map_editor, events=evs)

    assert called["mouse"] == 0
