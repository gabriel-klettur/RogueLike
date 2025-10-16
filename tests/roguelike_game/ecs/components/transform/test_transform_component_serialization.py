import dataclasses

from roguelike_game.ecs.components.transform.scale import Scale
from roguelike_game.ecs.components.transform.movement_speed import MovementSpeed


def test_scale_and_movement_speed_asdict():
    s = Scale(1.5)
    m = MovementSpeed(2.0)
    assert dataclasses.asdict(s) == {"scale": 1.5}
    assert dataclasses.asdict(m) == {"speed": 2.0}
