from roguelike_engine.buildings.building_model import BuildingModel


def test_collision_map_setter_invalidates_cache(pygame_init, patch_loader):
    patch_loader(size=(64, 64))
    m = BuildingModel(rel_x=0, rel_y=0, image_path="dummy.png", solid=True)
    m.collision_map = [["#", "."], [".", "."]]
    first = m.collision_tiles[:]  # build cache
    assert len(first) == 1
    # Change map
    m.collision_map = [["#", "#"], ["#", "#"]]
    second = m.collision_tiles
    assert len(second) == 4
