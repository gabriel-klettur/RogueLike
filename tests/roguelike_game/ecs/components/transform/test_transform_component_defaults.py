from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.transform.velocity import Velocity


def test_position_construction():
    p = Position(10.5, -3.25)
    assert p.x == 10.5
    assert p.y == -3.25


def test_velocity_defaults_and_overrides():
    v0 = Velocity()
    assert v0.vx == 0
    assert v0.vy == 0

    v1 = Velocity(3.0, -1.0)
    assert v1.vx == 3.0
    assert v1.vy == -1.0
