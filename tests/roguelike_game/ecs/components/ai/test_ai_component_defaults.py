from roguelike_game.ecs.components.ai.aggro_range import AggroRange
from roguelike_game.ecs.components.ai.in_combat import InCombat


def test_aggro_range_construction():
    ar = AggroRange(radius=5)
    assert ar.radius == 5


def test_in_combat_is_dataclass_marker():
    # InCombat is a dataclass without fields (marker component)
    inc = InCombat()
    assert isinstance(inc, InCombat)
