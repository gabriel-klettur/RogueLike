from types import SimpleNamespace

import pygame

from roguelike_engine.input.keyboard import handle_keyboard
from roguelike_engine.config.config_camera import ALLOWED_ZOOMS


class Camera(SimpleNamespace):
    pass


def _keydown(key: int):
    return pygame.event.Event(pygame.KEYDOWN, {"key": key})


def test_keyboard_zoom_repeat_hits_allowed_bounds():
    cam = Camera(zoom=1.0, offset_x=0.0, offset_y=0.0, screen_width=800, screen_height=600)
    state = SimpleNamespace()

    # Repetir "+" hasta máximo permitido
    for _ in range(20):
        handle_keyboard(_keydown(pygame.K_PLUS), state, cam, None, None, None, None, None, None, None)
    assert cam.zoom == ALLOWED_ZOOMS[-1]

    # Repetir "-" hasta mínimo permitido
    for _ in range(20):
        handle_keyboard(_keydown(pygame.K_MINUS), state, cam, None, None, None, None, None, None, None)
    assert cam.zoom == ALLOWED_ZOOMS[0]
