from types import SimpleNamespace

import pygame

from roguelike_engine.input.events import handle_events


def test_unknown_event_type_is_ignored_without_crash():
    state = SimpleNamespace(running=True)
    camera = clock = menu = map_manager = entities = None

    class _Ed:
        editor_state = SimpleNamespace(active=False)
        handler = SimpleNamespace(handle=lambda *a, **k: None)

    tiles_editor = buildings_editor = map_editor = _Ed()

    # Use an arbitrary event type not handled by our engine
    UNKNOWN = pygame.USEREVENT + 123
    evs = [pygame.event.Event(UNKNOWN, {"foo": 1})]
    # Should not raise and state.running unchanged
    handle_events(state, camera, clock, menu, map_manager, entities, tiles_editor, buildings_editor, map_editor, events=evs)
    assert state.running is True
