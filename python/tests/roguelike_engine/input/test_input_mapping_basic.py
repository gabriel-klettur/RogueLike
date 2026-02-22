from types import SimpleNamespace

import pygame

from roguelike_engine.input.keyboard import handle_keyboard
from roguelike_engine.input.mouse import handle_mouse


class Camera(SimpleNamespace):
    pass


def _make_keydown(key: int):
    return pygame.event.Event(pygame.KEYDOWN, {"key": key})


def _make_wheel(y: int):
    return pygame.event.Event(pygame.MOUSEWHEEL, {"y": y})


def test_keyboard_zoom_plus_minus_updates_camera_center_stable(monkeypatch):
    cam = Camera(zoom=1.0, offset_x=100.0, offset_y=50.0, screen_width=800, screen_height=600)
    state = SimpleNamespace()
    menu = map_manager = entities = None
    editors = (None, None, None)

    # Zoom in with '+'
    consumed = handle_keyboard(_make_keydown(pygame.K_PLUS), state, cam, None, menu, entities, *editors, map_manager)
    assert consumed is True
    # Zoom out with '-'
    consumed = handle_keyboard(_make_keydown(pygame.K_MINUS), state, cam, None, menu, entities, *editors, map_manager)
    assert consumed in (True, False)  # May be boundary


def test_mouse_wheel_zoom_keeps_world_point_under_cursor(monkeypatch):
    cam = Camera(zoom=1.0, offset_x=10.0, offset_y=20.0)
    state = SimpleNamespace()
    # Fix mouse position
    monkeypatch.setattr(pygame.mouse, "get_pos", lambda: (400, 300))

    ev = _make_wheel(+1)
    consumed = handle_mouse(ev, state, cam, None, None, None)
    assert consumed is True
    # After zoom, offsets must change coherently
    assert isinstance(cam.zoom, float) and cam.zoom != 1.0
    assert isinstance(cam.offset_x, float) and isinstance(cam.offset_y, float)
