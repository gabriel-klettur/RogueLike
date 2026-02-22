from roguelike_engine.buildings.building_model import BuildingModel


def test_build_collision_tiles_large_map_linear(pygame_init, patch_loader):
    patch_loader(size=(640, 640))
    m = BuildingModel(rel_x=0, rel_y=0, image_path="dummy.png", solid=True)
    # 50x50 map with diagonal solids
    size = 50
    cmap = [["#" if r == c else "." for c in range(size)] for r in range(size)]
    m.collision_map = cmap
    tiles = m.collision_tiles
    assert len(tiles) == size  # one per diagonal
