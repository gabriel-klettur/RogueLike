import pytest
from roguelike_game.factories.player import calibrator
from roguelike_engine.config.config_tiles import TILE_SIZE


class DummyFrame:
    def __init__(self, w, h):
        self._w = w
        self._h = h

    def get_size(self):
        return (self._w, self._h)


def test_calibrate_no_frame():
    assert calibrator.calibrate_tile_position(3, 4, None) == (3 * TILE_SIZE, 4 * TILE_SIZE)


def test_calibrate_with_frame():
    frame = DummyFrame(20, 40)
    from roguelike_game.factories.player.config import FEET_HEIGHT_DIVISOR
    feet_h = 40 // FEET_HEIGHT_DIVISOR
    half_feet = feet_h // 2
    cx = 3 * TILE_SIZE + TILE_SIZE // 2
    cy = 4 * TILE_SIZE + TILE_SIZE // 2
    expected_x = cx - (20 // 2)
    expected_y = cy - (40 - half_feet)
    assert calibrator.calibrate_tile_position(3, 4, frame) == (expected_x, expected_y)
