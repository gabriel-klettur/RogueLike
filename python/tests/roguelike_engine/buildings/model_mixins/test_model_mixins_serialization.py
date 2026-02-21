import pytest

from roguelike_engine.buildings.building_model import BuildingModel


def test_model_pickle_state_roundtrip(pygame_init, patch_loader):
    patch_loader(size=(40, 60))
    m = BuildingModel(rel_x=3, rel_y=4, image_path="dummy.png", solid=True, split_ratio=0.25, scale=(40, 60))
    m.zone = "lobby"
    m.collision_map = [["#"]]
    m.collider_scope = "CU"
    m.set_images_by_state({"healthy": "dummy.png"}, initial_state="healthy")
    state = m.__getstate__()

    # Recreate and restore
    patch_loader(size=(40, 60))
    n = BuildingModel(rel_x=0, rel_y=0, image_path="dummy.png", solid=False)
    n.__setstate__(state)

    # Key fields restored
    assert (n.rel_x, n.rel_y, n.zone) == (3, 4, "lobby")
    assert n.collider_scope == "CU"
    assert n.image is not None and n.image.get_size() == (40, 60)
