from roguelike_engine.buildings.services.collisions import resample_collision_map


def test_resample_same_size_returns_copy_not_alias():
    old = [["#", "."], [".", "."]]
    new = resample_collision_map(old, 2, 2)
    assert new == old and new is not old and all(n is not o for n, o in zip(new, old))
