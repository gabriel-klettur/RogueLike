from roguelike_game.ecs.components.transform.scale import Scale
from roguelike_game.ecs.components.transform.z_layer import ZLayer
from roguelike_game.ecs.components.transform.movement_speed import MovementSpeed


def test_scale_default_and_override():
    s0 = Scale()
    assert s0.scale == 1.0
    s1 = Scale(2.5)
    assert s1.scale == 2.5


def test_zlayer_construction():
    z = ZLayer(7)
    assert z.layer == 7


def test_movement_speed_default_and_override():
    m0 = MovementSpeed()
    assert m0.speed == 1.0
    m1 = MovementSpeed(3.0)
    assert m1.speed == 3.0
