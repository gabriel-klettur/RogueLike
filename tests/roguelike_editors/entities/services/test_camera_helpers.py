from types import SimpleNamespace
from roguelike_editors.entities.services.camera_helpers import screen_to_world, screen_to_tile


def test_screen_to_world_and_tile():
    cam = SimpleNamespace(zoom=2.0, offset_x=10.0, offset_y=5.0)

    wx, wy = screen_to_world(cam, 20, 10)
    assert (wx, wy) == (20/2.0 + 10.0, 10/2.0 + 5.0)

    tx, ty = screen_to_tile(cam, 20, 10, tile_size=8)
    assert (tx, ty) == (int((20/2.0 + 10.0)//8), int((10/2.0 + 5.0)//8))
