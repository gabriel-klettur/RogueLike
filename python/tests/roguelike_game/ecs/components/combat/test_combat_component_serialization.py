import dataclasses

from roguelike_game.ecs.components.combat.health import Health
from roguelike_game.ecs.components.combat.energy import Energy
from roguelike_game.ecs.components.combat.mana import Mana


def test_health_energy_mana_asdict():
    h = Health(current_hp=5, max_hp=10)
    e = Energy(current_energy=2, max_energy=3)
    m = Mana(current_mana=1, max_mana=6)
    assert dataclasses.asdict(h) == {"current_hp": 5, "max_hp": 10}
    assert dataclasses.asdict(e) == {"current_energy": 2, "max_energy": 3}
    assert dataclasses.asdict(m) == {"current_mana": 1, "max_mana": 6}
