import math
import time
import unittest

from roguelike_game.ecs.systems.fsm.states.attack_state import AttackState
from roguelike_game.config.spells_config import SPELLS
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.combat.health import Health
from roguelike_game.ecs.components.combat.melee_range import MeleeRange


class EntityProxy:
    def __init__(self, world, eid):
        self.world = world
        self.id = eid


class WorldStub:
    def __init__(self):
        self.components = {
            'Position': {},
            'Health': {},
            'MeleeRange': {},
            'HitboxComponent': {},
            'SlashEmitterComponent': {},
            'NPCAttackCooldown': {},
        }
        self._next = 10
        self.player_entity = None

    def create_entity(self):
        eid = self._next
        self._next += 1
        return eid


class TestHostileSlashUsage(unittest.TestCase):
    def setUp(self):
        # Validate hostile_slash exists in SPELLS
        self.assertIn('hostile_slash', SPELLS)

    def test_attack_state_uses_hostile_slash_green_and_wider(self):
        world = WorldStub()
        npc = 1
        player = 2
        world.player_entity = player
        world.components['Position'][npc] = Position(100.0, 100.0)
        world.components['Health'][npc] = Health(current_hp=100, max_hp=100)
        world.components['MeleeRange'][npc] = MeleeRange(range=4)
        world.components['Position'][player] = Position(150.0, 100.0)
        world.components['Health'][player] = Health(current_hp=100, max_hp=100)

        state = AttackState()
        proxy = EntityProxy(world, npc)

        t0 = time.time()
        state.execute(proxy, dt=0.016)

        # Emitter attached to caster must reflect hostile_slash params
        em = world.components['SlashEmitterComponent'][npc]
        # Wider/longer than default: radius >= 36 and arc ~ 140°
        self.assertGreaterEqual(em.radius, 36)
        self.assertAlmostEqual(em.arc_range, math.radians(140), delta=math.radians(5))
        # Green color
        self.assertEqual(tuple(em.color), (0, 255, 100))

        # Cooldown should be from hostile_slash (0.7s)
        cd = world.components['NPCAttackCooldown'][npc]
        self.assertGreaterEqual(cd.next_time - t0, 0.65)
        self.assertLessEqual(cd.next_time - t0, 0.9)


if __name__ == '__main__':
    unittest.main()
