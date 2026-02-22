import pygame

from roguelike_engine.buildings.building_view import BuildingView
from roguelike_engine.buildings.building_model import BuildingModel


def test_scaling_cached_for_same_zoom(pygame_init, patch_loader, screen, fake_camera, monkeypatch):
    patch_loader(size=(16, 16))
    m = BuildingModel(rel_x=0, rel_y=0, image_path="dummy.png", solid=True)
    v = BuildingView(m, fake_camera)

    calls = {"n": 0}
    orig_scale = pygame.transform.scale

    def spy_scale(surf, size):
        calls["n"] += 1
        return orig_scale(surf, size)

    monkeypatch.setattr(pygame.transform, "scale", spy_scale)

    v.render_part(screen, top=True)
    v.render_part(screen, top=False)
    # For same zoom and split_ratio, scaling should happen only once
    assert calls["n"] == 1
