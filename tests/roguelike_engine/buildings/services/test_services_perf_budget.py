from roguelike_engine.buildings.services.collisions import resample_collision_map


def test_resample_large_map_reasonable():
    size = 60
    old = [["#" if (r + c) % 3 == 0 else "." for c in range(size)] for r in range(size)]
    new = resample_collision_map(old, size // 2, size // 2)
    assert len(new) == size // 2 and len(new[0]) == size // 2
