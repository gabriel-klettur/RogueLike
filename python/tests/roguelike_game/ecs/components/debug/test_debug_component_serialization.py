import dataclasses

from roguelike_game.ecs.components.debug.movement_debug import MovementDebug


def test_movement_debug_asdict_serialization():
    dbg = MovementDebug(last_pos=(1.0, 2.0), last_dir=(0.0, 1.0), stuck_frames=3)
    data = dataclasses.asdict(dbg)
    assert data == {
        "last_pos": (1.0, 2.0),
        "last_dir": (0.0, 1.0),
        "stuck_frames": 3,
    }
