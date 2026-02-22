import pygame

from roguelike_engine.buildings.building_view import BuildingView
from roguelike_engine.buildings.building_model import BuildingModel


def test_view_clears_caches_when_image_changes(pygame_init, patch_loader, screen, fake_camera, monkeypatch):
    patch_loader(size=(8, 8))
    m = BuildingModel(rel_x=0, rel_y=0, image_path="dummy.png", solid=True)
    v = BuildingView(m, fake_camera)

    calls = {"n": 0}
    orig_scale = pygame.transform.scale

    def spy_scale(surf, size):
        calls["n"] += 1
        return orig_scale(surf, size)

    monkeypatch.setattr(pygame.transform, "scale", spy_scale)

    v.render_part(screen, top=True)  # first time scales once
    assert calls["n"] == 1

    # Change model image -> should invalidate and rescale again
    m.image = pygame.Surface((8, 8), flags=pygame.SRCALPHA)
    v.render_part(screen, top=True)
    assert calls["n"] >= 2
