import unittest

from roguelike_game.ecs.systems.combat.melee.melee_combat_system import MeleeCombatSystem
from roguelike_game.ecs.components.combat.combat_stats import CombatStats
from roguelike_game.ecs.components.core.identity import Identity, Faction
from roguelike_game.ecs.components.combat.allow_friendly_fire import AllowFriendlyFire


class WorldStub:
    def __init__(self):
        self.components = {
            'CombatStats': {},
            'Identity': {},
            'MonsterArchetype': {},
            'WantsToMelee': {},
            'LastAttacker': {},
        }


class Intent:
    def __init__(self, attacker, target):
        self.attacker = attacker
        self.target = target


class TestFriendlyFireMelee(unittest.TestCase):
    def test_same_faction_skips_damage_and_cleans_event(self):
        world = WorldStub()
        sys = MeleeCombatSystem(perf_log=None)

        a = 1
        b = 2
        world.components['MonsterArchetype'][a] = object()
        world.components['MonsterArchetype'][b] = object()
        world.components['Identity'][a] = Identity(id=a, name='A', title='', faction=Faction.EVIL)
        world.components['Identity'][b] = Identity(id=b, name='B', title='', faction=Faction.EVIL)
        world.components['CombatStats'][a] = CombatStats(current_hp=100, max_hp=100, power=10, defense=0)
        world.components['CombatStats'][b] = CombatStats(current_hp=100, max_hp=100, power=5, defense=0)
        world.components['WantsToMelee'][99] = Intent(a, b)

        sys.update(world, camera=None)

        # Event cleaned, no damage applied
        self.assertNotIn(99, world.components['WantsToMelee'])
        self.assertEqual(world.components['CombatStats'][b].current_hp, 100)

    def test_allow_friendly_fire_bypasses_filter(self):
        world = WorldStub()
        sys = MeleeCombatSystem(perf_log=None)

        a = 1
        b = 2
        world.components['MonsterArchetype'][a] = object()
        world.components['MonsterArchetype'][b] = object()
        world.components['Identity'][a] = Identity(id=a, name='A', title='', faction=Faction.EVIL)
        world.components['Identity'][b] = Identity(id=b, name='B', title='', faction=Faction.EVIL)
        world.components['CombatStats'][a] = CombatStats(current_hp=100, max_hp=100, power=12, defense=0)
        world.components['CombatStats'][b] = CombatStats(current_hp=100, max_hp=100, power=5, defense=2)
        world.components.setdefault('AllowFriendlyFire', {})[a] = AllowFriendlyFire(enabled=True)
        world.components['WantsToMelee'][100] = Intent(a, b)

        sys.update(world, camera=None)

        # Damage applied: max(0, power + bonus - defense) = max(0, 12 - 2) = 10
        self.assertEqual(world.components['CombatStats'][b].current_hp, 90)
        self.assertNotIn(100, world.components['WantsToMelee'])


if __name__ == '__main__':
    unittest.main()
