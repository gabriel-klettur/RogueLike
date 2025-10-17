import math
import pygame

from roguelike_editors.map.events.camera import handle_zoom


class CamStub:
    def __init__(self, zoom: float = 1.0, offset_x: float = 0.0, offset_y: float = 0.0):
        self.zoom = float(zoom)
        self.offset_x = float(offset_x)
        self.offset_y = float(offset_y)
        self.screen_width = 800
        self.screen_height = 600


def _wheel(y: int) -> pygame.event.Event:
    return pygame.event.Event(pygame.MOUSEWHEEL, {"y": y})


def test_zoom_increases_and_decreases_monotonically(monkeypatch):
    cam = CamStub(zoom=1.0, offset_x=10.0, offset_y=20.0)
    state = type("S", (), {})()

    # Anchor mouse
    monkeypatch.setattr(pygame.mouse, "get_pos", lambda: (400, 300))

    z0 = cam.zoom
    handle_zoom(_wheel(+1), cam, state)  # zoom in
    z1 = cam.zoom
    assert z1 > z0
    # factor is ~1.1; allow small fp tolerance
    assert math.isclose(z1, z0 * 1.1, rel_tol=1e-12, abs_tol=0.0)

    handle_zoom(_wheel(-1), cam, state)  # zoom out (should invert prior step)
    z2 = cam.zoom
    assert z2 < z1
    assert math.isclose(z2, z1 / 1.1, rel_tol=1e-12, abs_tol=0.0)
