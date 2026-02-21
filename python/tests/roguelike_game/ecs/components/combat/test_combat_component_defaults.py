from roguelike_game.ecs.components.combat.health import Health
from roguelike_game.ecs.components.combat.energy import Energy
from roguelike_game.ecs.components.combat.mana import Mana
from roguelike_game.ecs.components.combat.combat_stats import CombatStats
from roguelike_game.ecs.components.combat.inventory import InventoryComponent as CombatInventoryComponent
import dataclasses


def test_health_energy_mana_dataclasses():
    h = Health(current_hp=10, max_hp=20)
    e = Energy(current_energy=5, max_energy=10)
    m = Mana(current_mana=3, max_mana=9)

    assert dataclasses.asdict(h) == {"current_hp": 10, "max_hp": 20}
    assert dataclasses.asdict(e) == {"current_energy": 5, "max_energy": 10}
    assert dataclasses.asdict(m) == {"current_mana": 3, "max_mana": 9}


def test_combat_stats_construction():
    cs = CombatStats(current_hp=8, max_hp=12, power=4, defense=2)
    assert cs.current_hp == 8
    assert cs.max_hp == 12
    assert cs.power == 4
    assert cs.defense == 2


def test_combat_inventory_component_defaults():
    inv = CombatInventoryComponent()
    assert isinstance(inv.items, list)
    assert inv.items == []
