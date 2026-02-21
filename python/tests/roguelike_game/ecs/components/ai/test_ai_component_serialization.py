import dataclasses

from roguelike_game.ecs.components.ai.in_combat import InCombat


def test_in_combat_asdict_serialization():
    inc = InCombat()
    data = dataclasses.asdict(inc)
    assert data == {}
