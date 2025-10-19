from roguelike_engine.buildings.model_utils.collision_ops import build_collision_tiles


def test_build_collision_tiles_many_cells_linear():
    size = 100
    cmap = [["#" for _ in range(size)] for _ in range(size)]
    rects = build_collision_tiles(cmap, base_x=0, base_y=0, tile_size=8)
    assert len(rects) == size * size
