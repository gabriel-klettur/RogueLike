from types import SimpleNamespace

import pygame

from roguelike_engine.input.keyboard import handle_keyboard
from roguelike_engine.input.mouse import handle_mouse


def test_keyboard_non_keydown_is_ignored():
    cam = SimpleNamespace(zoom=1.0, offset_x=0.0, offset_y=0.0, screen_width=800, screen_height=600)
    state = SimpleNamespace()
    ev = pygame.event.Event(pygame.KEYUP, {"key": pygame.K_PLUS})
    assert handle_keyboard(ev, state, cam, None, None, None, None, None, None, None) is False


def test_keyboard_handles_bad_camera_values_without_crash():
    # Invalid screen dims cause int() to fail internally; handler must swallow and return False
    cam = SimpleNamespace(zoom=1.0, offset_x=0.0, offset_y=0.0, screen_width="bad", screen_height="bad")
    state = SimpleNamespace()
    ev = pygame.event.Event(pygame.KEYDOWN, {"key": pygame.K_PLUS})
    assert handle_keyboard(ev, state, cam, None, None, None, None, None, None, None) is False


def test_mouse_wheel_get_pos_failure_is_swallowed(monkeypatch):
    cam = SimpleNamespace(zoom=1.0, offset_x=0.0, offset_y=0.0)
    state = SimpleNamespace()
    # Force pygame.mouse.get_pos to raise
    def _boom():
        raise RuntimeError("boom")
    monkeypatch.setattr(pygame.mouse, "get_pos", _boom)
    ev = pygame.event.Event(pygame.MOUSEWHEEL, {"y": 1})
    assert handle_mouse(ev, state, cam, None, None, None) is False


def test_mmb_panning_errors_do_not_crash():
    # Use bare object() as state so attribute assignment fails inside handler
    state = object()
    cam = SimpleNamespace(offset_x=0.0, offset_y=0.0, zoom=1.0)

    down = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {"button": 2})
    move = pygame.event.Event(pygame.MOUSEMOTION, {})
    up = pygame.event.Event(pygame.MOUSEBUTTONUP, {"button": 2})

    # All should return False instead of crashing
    assert handle_mouse(down, state, cam, None, None, None, mmb_pan_enabled=True) is False
    assert handle_mouse(move, state, cam, None, None, None, mmb_pan_enabled=True) is False
    assert handle_mouse(up, state, cam, None, None, None, mmb_pan_enabled=True) is False
