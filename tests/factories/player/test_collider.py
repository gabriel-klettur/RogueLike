import pytest
from roguelike_game.factories.player import collider
from roguelike_game.factories.player.config import FEET_WIDTH_DIVISOR, FEET_HEIGHT_DIVISOR

class FakeSurface:
    def __init__(self, w, h):
        self._w = w
        self._h = h
    def get_size(self):
        return (self._w, self._h)

class DummyMaskCollider:
    def __init__(self, mask, offset_x, offset_y):
        self.mask = mask
        self.offset_x = offset_x
        self.offset_y = offset_y

class DummyCollider:
    def __init__(self, w, h, offset_x, offset_y):
        self.w = w
        self.h = h
        self.offset_x = offset_x
        self.offset_y = offset_y

@pytest.fixture(autouse=True)
def patch(monkeypatch):
    # Stub pygame.mask.from_surface
    import pygame
    monkeypatch.setattr(pygame.mask, 'from_surface', lambda s: 'dummy_mask')
    # Patch component classes in collider module
    monkeypatch.setattr(collider, 'MaskCollider', DummyMaskCollider)
    monkeypatch.setattr(collider, 'Collider', DummyCollider)
    yield


def test_create_body_and_feet():
    w, h = 50, 100
    surface = FakeSurface(w, h)
    multi = collider.create_body_and_feet(surface)

    # Body collider
    body = multi.colliders['body']
    assert isinstance(body, DummyMaskCollider)
    assert body.mask == 'dummy_mask'
    assert body.offset_x == 0 and body.offset_y == 0

    # Feet collider
    feet = multi.colliders['feet']
    expected_w = w // FEET_WIDTH_DIVISOR
    expected_h = h // FEET_HEIGHT_DIVISOR
    expected_offset_x = (w - expected_w) // 2
    expected_offset_y = h - expected_h
    assert isinstance(feet, DummyCollider)
    assert feet.w == expected_w and feet.h == expected_h
    assert feet.offset_x == expected_offset_x and feet.offset_y == expected_offset_y
