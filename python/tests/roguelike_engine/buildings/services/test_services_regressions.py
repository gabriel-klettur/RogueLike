from roguelike_engine.buildings.services.collisions import resample_collision_map


def test_resample_pools_any_solid_in_block():
    old = [
        [".", ".", ".", "."],
        [".", "#", ".", "."],
        [".", ".", ".", "."],
        [".", ".", ".", "."],
    ]
    new = resample_collision_map(old, 2, 2)
    # Top-left quadrant has a '#'
    assert new[0][0] == "#"
