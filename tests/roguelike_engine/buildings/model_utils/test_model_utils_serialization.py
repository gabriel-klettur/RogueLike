from roguelike_engine.buildings.building_model import BuildingModel


def test_pickling_ops_exclude_surfaces(pygame_init, patch_loader):
    patch_loader(size=(32, 32))
    m = BuildingModel(rel_x=1, rel_y=2, image_path="dummy.png", solid=True)
    state = m.__getstate__()
    # Ensure no pygame.Surface in state dict
    assert "image" not in state and "_mask_full" not in state
    assert "collision_map" in state and isinstance(state["collision_map"], list)
