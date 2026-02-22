import pytest

from roguelike_engine.buildings.building_model import BuildingModel


def test_empty_collision_map_returns_empty_tiles(pygame_init, patch_loader):
    patch_loader(size=(32, 32))
    m = BuildingModel(rel_x=0, rel_y=0, image_path="dummy.png", solid=True)
    m.collision_map = []
    assert m.collision_tiles == []
    assert m.collision_tile_objs == []


def test_get_full_mask_none_when_no_image(pygame_init, patch_loader):
    patch_loader(size=(32, 32))
    m = BuildingModel(rel_x=0, rel_y=0, image_path="dummy.png", solid=True)
    # Simulate missing image
    m.image = None
    assert m.get_full_mask() is None
