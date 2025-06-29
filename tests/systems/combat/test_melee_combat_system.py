# Path: tests/systems/combat/test_melee_combat_system.py
import pytest
from roguelike_game.ecs.components.combat.combat_stats import CombatStats
from roguelike_game.ecs.components.combat.melee_weapon import MeleeWeapon
from roguelike_game.ecs.components.ai.wants_to_melee import WantsToMelee
from roguelike_game.ecs.core.manager import ECSWorld
from roguelike_game.ecs.systems.combat.melee.melee_combat_system import MeleeCombatSystem


def test_melee_damage_in_range(world):
    eid_att = world.create_entity()
    eid_def = world.create_entity()
    world.components['CombatStats'][eid_att] = CombatStats(10, 10, power=5, defense=0)
    world.components['CombatStats'][eid_def] = CombatStats(10, 10, power=0, defense=2)
    world.components['MeleeWeapon'][eid_att] = MeleeWeapon(damage=3, cooldown=0)
    # Intento de ataque
    world.components['WantsToMelee'][eid_att] = WantsToMelee(attacker=eid_att, target=eid_def)
    system = MeleeCombatSystem(perf_log=None)
    system.update(world)
    # Cálculo: 5 power + 3 bonus - 2 defense = 6 damage
    assert world.components['CombatStats'][eid_def].current_hp == 10 - 6


def test_melee_damage_not_negative(world):
    eid_att = world.create_entity()
    eid_def = world.create_entity()
    world.components['CombatStats'][eid_att] = CombatStats(10, 10, power=1, defense=0)
    world.components['CombatStats'][eid_def] = CombatStats(10, 10, power=0, defense=5)
    # without weapon
    world.components['WantsToMelee'][eid_att] = WantsToMelee(attacker=eid_att, target=eid_def)
    system = MeleeCombatSystem(perf_log=None)
    system.update(world)
    # defense > power so damage = 0
    assert world.components['CombatStats'][eid_def].current_hp == 10


def test_wants_to_melee_cleared(world):
    eid_att = world.create_entity()
    eid_def = world.create_entity()
    world.components['CombatStats'][eid_att] = CombatStats(10, 10, power=1, defense=0)
    world.components['CombatStats'][eid_def] = CombatStats(10, 10, power=0, defense=0)
    world.components['WantsToMelee'][eid_att] = WantsToMelee(attacker=eid_att, target=eid_def)
    system = MeleeCombatSystem(perf_log=None)
    system.update(world)
    assert eid_att not in world.components['WantsToMelee']