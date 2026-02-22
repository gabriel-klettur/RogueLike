import math
import pygame
import pytest

from roguelike_editors.map.events.camera import handle_zoom


class CamStub:
    def __init__(self, zoom: float = 1.0, offset_x: float = 0.0, offset_y: float = 0.0):
        self.zoom = float(zoom)
        self.offset_x = float(offset_x)
        self.offset_y = float(offset_y)
        # Attributes used by other camera APIs but not needed by handle_zoom
        self.screen_width = 800
        self.screen_height = 600


def _wheel(y: int) -> pygame.event.Event:
    return pygame.event.Event(pygame.MOUSEWHEEL, {"y": y})


def test_zoom_out_unlimited_hits_epsilon_floor(monkeypatch):
    cam = CamStub(zoom=1.0, offset_x=100.0, offset_y=50.0)
    state = type("S", (), {})()

    # Anchor mouse somewhere in the middle of the screen
    mx, my = 400, 300
    monkeypatch.setattr(pygame.mouse, "get_pos", lambda: (mx, my))

    # Repeated zoom out should clamp at epsilon (1e-6), not go below or crash
    for _ in range(200):
        handle_zoom(_wheel(-1), cam, state)

    assert cam.zoom >= 1e-6
    assert cam.zoom <= 1.000001e-6  # within a small tolerance of the epsilon floor


@pytest.mark.parametrize("wheel_y", [+1, -1])
def test_zoom_preserves_world_point_under_mouse(monkeypatch, wheel_y):
    cam = CamStub(zoom=1.0, offset_x=123.4, offset_y=56.7)
    state = type("S", (), {})()

    # Choose an arbitrary mouse position
    mx, my = 321, 222
    monkeypatch.setattr(pygame.mouse, "get_pos", lambda: (mx, my))

    # Compute world point under mouse BEFORE zoom
    z0 = cam.zoom
    wx = mx / z0 + cam.offset_x
    wy = my / z0 + cam.offset_y

    # Apply zoom event
    handle_zoom(_wheel(wheel_y), cam, state)

    # After zoom, offsets should maintain the same world point under the cursor
    z1 = cam.zoom
    expected_ox = wx - mx / z1
    expected_oy = wy - my / z1

    assert math.isfinite(cam.offset_x) and math.isfinite(cam.offset_y)
    assert pytest.approx(cam.offset_x, rel=1e-9, abs=1e-9) == expected_ox
    assert pytest.approx(cam.offset_y, rel=1e-9, abs=1e-9) == expected_oy
