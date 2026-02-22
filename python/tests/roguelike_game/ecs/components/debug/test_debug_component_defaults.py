from roguelike_game.ecs.components.debug.movement_debug import MovementDebug


def test_movement_debug_defaults():
    dbg = MovementDebug()
    assert dbg.last_pos is None
    assert dbg.last_dir is None
    assert dbg.stuck_frames == 0
