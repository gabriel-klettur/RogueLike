from types import SimpleNamespace

import pygame

from roguelike_engine.input.events import handle_events


def test_handle_events_uses_provided_event_list(monkeypatch):
    called = {"get": False}

    def _boom():
        called["get"] = True
        raise AssertionError("pygame.event.get must not be called when events list is provided")

    monkeypatch.setattr(pygame.event, "get", _boom)

    state = SimpleNamespace(running=True)
    camera = clock = menu = map_manager = entities = None

    class _Ed:
        editor_state = SimpleNamespace(active=False)
        handler = SimpleNamespace(handle=lambda *a, **k: None)

    tiles_editor = buildings_editor = map_editor = _Ed()

    evs = [pygame.event.Event(pygame.QUIT, {})]
    handle_events(state, camera, clock, menu, map_manager, entities, tiles_editor, buildings_editor, map_editor, events=evs)

    assert state.running is False
    assert called["get"] is False
