import time
import unittest

from roguelike_game.ecs.systems.fsm.states.attack_state import AttackState
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
            'NPCAttackCooldown': {},
        }
        self._next = 100
        self.player_entity = None

    def create_entity(self):
        eid = self._next
        self._next += 1
        return eid


class TestAttackStateSlash(unittest.TestCase):
    def test_attack_state_spawns_slash_towards_player_and_respects_cooldown(self):
        world = WorldStub()

        # NPC and Player
        npc = 1
        player = 2
        world.player_entity = player
        world.components['Position'][npc] = Position(100.0, 100.0)
        world.components['Health'][npc] = Health(current_hp=100, max_hp=100)
        world.components['MeleeRange'][npc] = MeleeRange(range=3)  # generous tiles range

        world.components['Position'][player] = Position(140.0, 100.0)  # to the right
        world.components['Health'][player] = Health(current_hp=100, max_hp=100)

        # Execute AttackState without calling enter() to avoid animation/FSM dependencies
        state = AttackState()
        proxy = EntityProxy(world, npc)

        # First tick should spawn a slash hitbox targeting the player
        state.execute(proxy, dt=0.016)
        hitboxes = world.components['HitboxComponent']
        self.assertGreaterEqual(len(hitboxes), 1, "Expected at least one hitbox spawned by NPC slash")
        # Grab the first hitbox and verify rotate_with_owner=False (so it won't follow mouse)
        hb_eid, hb = next(iter(hitboxes.items()))
        self.assertFalse(hb.rotate_with_owner)
        # Direction should point roughly towards +X (player is to the right)
        self.assertGreaterEqual(hb.direction[0], 0.9)
        self.assertAlmostEqual(hb.direction[1], 0.0, delta=0.2)

        # Second immediate tick should NOT spawn another hitbox due to cooldown
        count_before = len(hitboxes)
        state.execute(proxy, dt=0.016)
        self.assertEqual(len(hitboxes), count_before)

        # Simulate waiting past cooldown (use slash.cooldown ~0.5s per config)
        # Advance NPCAttackCooldown to the past
        cd = world.components['NPCAttackCooldown'][npc]
        world.components['NPCAttackCooldown'][npc] = type(cd)(next_time=time.time() - 1)
        state.execute(proxy, dt=0.016)
        self.assertGreater(len(hitboxes), count_before)


if __name__ == '__main__':
    unittest.main()
