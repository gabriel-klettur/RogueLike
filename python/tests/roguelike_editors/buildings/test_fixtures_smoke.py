import pygame


def test_surface_factory(surface_factory):
    surf = surface_factory(32, 16, (255, 0, 0, 128))
    assert isinstance(surf, pygame.Surface)
    assert surf.get_size() == (32, 16)


def test_camera_apply_and_scale(camera):
    camera.zoom = 2.0
    camera.offset_x = 10
    camera.offset_y = 5
    assert camera.apply((10, 5)) == (0, 0)
    assert camera.apply((20, 15)) == (20, 20)
    assert camera.scale((10, 10)) == (20, 20)
