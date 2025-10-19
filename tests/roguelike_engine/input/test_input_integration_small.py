from types import SimpleNamespace

import pygame

from roguelike_engine.input.events import handle_events
from roguelike_engine.input.keyboard import handle_keyboard
from roguelike_engine.input.mouse import handle_mouse


def test_small_integration_keyboard_and_mouse_sequence(monkeypatch):
    # Minimal state and camera
    state = SimpleNamespace(running=True)
    camera = SimpleNamespace(offset_x=10.0, offset_y=20.0, zoom=1.0, screen_width=800, screen_height=600)

    class _Ed:
        editor_state = SimpleNamespace(active=False)
        handler = SimpleNamespace(handle=lambda *a, **k: None)

    tiles_editor = _Ed()
    buildings_editor = _Ed()
    map_editor = _Ed()

    # Fix mouse position for wheel calculations
    monkeypatch.setattr(pygame.mouse, "get_pos", lambda: (400, 300))

    # Feed a small mixed sequence: key '+', mouse wheel, and finally QUIT
    evs = [
        pygame.event.Event(pygame.KEYDOWN, {"key": pygame.K_PLUS}),
        pygame.event.Event(pygame.MOUSEWHEEL, {"y": +1}),
        pygame.event.Event(pygame.QUIT, {}),
    ]

    handle_events(state, camera, None, None, None, None, tiles_editor, buildings_editor, map_editor, events=evs)

    # After processing, we expect running flag cleared by QUIT and zoom adjusted by inputs
    assert state.running is False
    assert isinstance(camera.zoom, float) and camera.zoom >= 1.0
