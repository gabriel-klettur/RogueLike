import pytest
from roguelike_editors.tiles.common.view import screen_to_world, world_to_tile, screen_to_tile
from roguelike_engine.config.config_tiles import TILE_SIZE


class DummyCamera:
    def __init__(self, zoom, offset_x, offset_y):
        self.zoom = zoom
        self.offset_x = offset_x
        self.offset_y = offset_y


def test_screen_to_world_identity():
    cam = DummyCamera(zoom=1, offset_x=0, offset_y=0)
    pos = (100, 150)
    assert screen_to_world(pos, cam) == (100, 150)


def test_screen_to_world_scale_and_offset():
    cam = DummyCamera(zoom=2, offset_x=10, offset_y=-5)
    mx, my = 4, 6
    wx, wy = screen_to_world((mx, my), cam)
    assert wx == pytest.approx(mx / 2 + 10)
    assert wy == pytest.approx(my / 2 - 5)


def test_world_to_tile_basic():
    # exact multiples
    world_pos = (TILE_SIZE * 2, TILE_SIZE * 3)
    assert world_to_tile(world_pos) == (2, 3)


def test_world_to_tile_flooring():
    world_pos = (TILE_SIZE * 2 + 15, TILE_SIZE * 3 + 31)
    # int() before // ensures truncation
    expected_col = int(world_pos[0]) // TILE_SIZE
    expected_row = int(world_pos[1]) // TILE_SIZE
    assert world_to_tile(world_pos) == (expected_col, expected_row)


def test_screen_to_tile_integration():
    cam = DummyCamera(zoom=2, offset_x=0, offset_y=0)
    mx, my = TILE_SIZE * 3 * cam.zoom + 10, TILE_SIZE * 4 * cam.zoom + 20
    expected = world_to_tile(screen_to_world((mx, my), cam))
    result = screen_to_tile((mx, my), cam)
    assert result == expected
    assert isinstance(result[0], int) and isinstance(result[1], int)
