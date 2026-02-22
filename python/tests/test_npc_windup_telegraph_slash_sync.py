import math
import time
import unittest

from roguelike_game.ecs.systems.fsm.states.attack_state import AttackState
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.combat.health import Health
from roguelike_game.ecs.components.combat.melee_range import MeleeRange
from roguelike_game.ecs.components.transform.velocity import Velocity
from roguelike_game.ecs.components.ai.chase_target import ChaseTarget


class DummyFSM:
    def __init__(self):
        self.context = {}


class DummyNPCState:
    def __init__(self):
        self.fsm = DummyFSM()


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
            'NPCState': {},
            'TelegraphArc': {},
            'WindupOutline': {},
            'MonsterArchetype': {},
            'Velocity': {},
            'ChaseTarget': {},
        }
        self._next = 100
        self.player_entity = None

    def create_entity(self):
        eid = self._next
        self._next += 1
        return eid


def _norm(dx: float, dy: float):
    mag = math.hypot(dx, dy)
    if mag <= 1e-6:
        return 1.0, 0.0
    return dx / mag, dy / mag


class TestNPCWindupTelegraphSlashSync(unittest.TestCase):
    def setUp(self):
        self.world = WorldStub()
        # NPC and Player
        self.npc = 1
        self.player = 2
        self.world.player_entity = self.player
        self.world.components['NPCState'][self.npc] = DummyNPCState()
        # MonsterArchetype present (non-boss, use hostile_slash mapping by default)
        self.world.components['MonsterArchetype'][self.npc] = type('A', (), {'type': 'barbol'})()
        # Basic comps
        self.world.components['Position'][self.npc] = Position(100.0, 100.0)
        self.world.components['Health'][self.npc] = Health(current_hp=100, max_hp=100)
        self.world.components['MeleeRange'][self.npc] = MeleeRange(range=5)
        self.world.components['Position'][self.player] = Position(140.0, 100.0)
        self.world.components['Health'][self.player] = Health(current_hp=100, max_hp=100)
        # FSM context flags: ensure telegraph and short wind-up for tests
        ctx = self.world.components['NPCState'][self.npc].fsm.context
        ctx['use_attack_telegraph'] = True
        ctx['attack_interruptible'] = False
        ctx['attack_windup_s'] = 0.3

    def test_outline_and_telegraph_during_windup(self):
        state = AttackState()
        proxy = EntityProxy(self.world, self.npc)
        # First tick: should start wind-up, add outline and (with flag) telegraph
        state.execute(proxy, dt=0.016)
        self.assertIn(self.npc, self.world.components['WindupOutline'])
        self.assertIn(self.npc, self.world.components['TelegraphArc'])
        # Progress must be within (0,1]
        arc = self.world.components['TelegraphArc'][self.npc]
        prog = float(getattr(arc, 'progress', 0.0))
        self.assertGreaterEqual(prog, 0.0)
        self.assertLessEqual(prog, 1.0)
        # No hitbox yet (still winding up)
        self.assertEqual(len(self.world.components['HitboxComponent']), 0)

    def test_slash_fires_after_windup_and_matches_telegraph_direction(self):
        state = AttackState()
        proxy = EntityProxy(self.world, self.npc)
        # Start wind-up
        state.execute(proxy, dt=0.016)
        # Capture initial expected locked direction towards player
        npc_pos = self.world.components['Position'][self.npc]
        ply_pos = self.world.components['Position'][self.player]
        exp_dx, exp_dy = _norm(ply_pos.x - npc_pos.x, ply_pos.y - npc_pos.y)
        # Move player elsewhere to ensure direction was locked and not updated later
        self.world.components['Position'][self.player] = Position(100.0, 140.0)
        # Force end of wind-up by backdating attack_start
        ctx = self.world.components['NPCState'][self.npc].fsm.context
        ctx['attack_start'] = time.time() - 1.0
        # Fire
        before_hbs = set(self.world.components['HitboxComponent'].keys())
        state.execute(proxy, dt=0.016)
        after_hbs = set(self.world.components['HitboxComponent'].keys())
        new_hbs = list(after_hbs - before_hbs)
        self.assertGreaterEqual(len(new_hbs), 1, 'Expected a hitbox after wind-up completes')
        hb = self.world.components['HitboxComponent'][new_hbs[0]]
        # Direction approx equals locked expected
        self.assertAlmostEqual(hb.direction[0], exp_dx, delta=0.15)
        self.assertAlmostEqual(hb.direction[1], exp_dy, delta=0.15)
        # Telegraph cleared after attack
        self.assertNotIn(self.npc, self.world.components.get('TelegraphArc', {}))
        # Cooldown set
        cd = self.world.components['NPCAttackCooldown'].get(self.npc)
        self.assertIsNotNone(cd)
        self.assertGreater(getattr(cd, 'next_time', 0.0), time.time())

    def test_minimum_windup_enforced_when_config_zero(self):
        # Set zero wind-up but keep FSM context present; engine enforces min 0.2s
        ctx = self.world.components['NPCState'][self.npc].fsm.context
        ctx['attack_windup_s'] = 0.0
        state = AttackState()
        proxy = EntityProxy(self.world, self.npc)
        # Start wind-up (should not attack immediately)
        state.execute(proxy, dt=0.016)
        self.assertEqual(len(self.world.components['HitboxComponent']), 0)
        # Backdate attack_start less than 0.2s: still winding up
        ctx['attack_start'] = time.time() - 0.1
        state.execute(proxy, dt=0.016)
        self.assertEqual(len(self.world.components['HitboxComponent']), 0)
        # Backdate beyond 0.2s: now should fire
        ctx['attack_start'] = time.time() - 0.25
        state.execute(proxy, dt=0.016)
        self.assertGreaterEqual(len(self.world.components['HitboxComponent']), 1)

    def test_windup_freezes_movement_when_uninterruptible(self):
        state = AttackState()
        proxy = EntityProxy(self.world, self.npc)
        # Set non-zero velocity and an active chase target
        self.world.components['Velocity'][self.npc] = Velocity(10, 5)
        self.world.components['ChaseTarget'][self.npc] = ChaseTarget(self.player)
        # Start wind-up
        state.execute(proxy, dt=0.016)
        # During wind-up, Velocity should be reset to (0,0) and ChaseTarget removed
        vel = self.world.components['Velocity'][self.npc]
        self.assertEqual((vel.vx, vel.vy), (0, 0))
        self.assertNotIn(self.npc, self.world.components['ChaseTarget'])

    def test_windup_allows_movement_when_interruptible_true(self):
        # Enable interruptible attacks
        ctx = self.world.components['NPCState'][self.npc].fsm.context
        ctx['attack_interruptible'] = True
        state = AttackState()
        proxy = EntityProxy(self.world, self.npc)
        # Set non-zero velocity and an active chase target
        self.world.components['Velocity'][self.npc] = Velocity(7, -3)
        self.world.components['ChaseTarget'][self.npc] = ChaseTarget(self.player)
        # Start wind-up
        state.execute(proxy, dt=0.016)
        # During wind-up, movement should NOT be forcibly reset/removed
        vel = self.world.components['Velocity'][self.npc]
        self.assertEqual((vel.vx, vel.vy), (7, -3))
        self.assertIn(self.npc, self.world.components['ChaseTarget'])


if __name__ == '__main__':
    unittest.main()
