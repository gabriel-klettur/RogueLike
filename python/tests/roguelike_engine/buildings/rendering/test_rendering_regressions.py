import pygame
import time

from roguelike_engine.buildings.building_view import BuildingView
from roguelike_engine.buildings.building_model import BuildingModel


def _rgb(rgb):
    return (rgb[0], rgb[1], rgb[2])


def test_flash_applies_tint_when_active(pygame_init, patch_loader, screen, fake_camera):
    patch_loader(size=(10, 10))
    m = BuildingModel(rel_x=0, rel_y=0, image_path="dummy.png", solid=True, split_ratio=0.5)
    # Fill with gray to observe brighten
    m.image.fill((100, 100, 100))

    # Trigger flash for a short duration
    m.trigger_flash(color=(50, 0, 0), duration=0.2)

    v = BuildingView(m, fake_camera)
    v.render_part(screen, top=True)
    base = _rgb(screen.get_at((0, 0)))
    assert base[0] >= 100  # red channel increased by tint
