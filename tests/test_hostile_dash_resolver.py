import unittest

from roguelike_game.ecs.systems.combat.spells.resolvers import SPELL_RESOLVERS
from roguelike_game.config.spells_config import SPELLS
from roguelike_game.ecs.components.transform.position import Position


class WorldStub:
    def __init__(self):
        self.components = {
            'Position': {},
            'DashComponent': {},
            'DashEmitterComponent': {},
        }
        self._next = 1

    def create_entity(self):
        eid = self._next
        self._next += 1
        return eid


class TestHostileDashResolver(unittest.TestCase):
    def test_resolver_spawns_dash_and_green_emitter(self):
        # Ensure spell exists and resolver is registered
        self.assertIn('hostile_dash', SPELLS)
        self.assertIn('hostile_dash', SPELL_RESOLVERS)

        world = WorldStub()
        caster = world.create_entity()
        target = world.create_entity()
        world.components['Position'][caster] = Position(100.0, 100.0)
        world.components['Position'][target] = Position(140.0, 100.0)

        resolver = SPELL_RESOLVERS['hostile_dash']
        cfg = SPELLS['hostile_dash']
        spawn_meta = {'target_eid': target}

        resolver.resolve(world, caster, spawn_meta, cfg, camera=None)

        # DashComponent should exist for caster
        self.assertIn(caster, world.components['DashComponent'])
        dc = world.components['DashComponent'][caster]
        self.assertGreaterEqual(dc.speed, 2300.0)
        self.assertGreaterEqual(dc.duration, 0.2)
        # Direction should be towards +X roughly
        self.assertGreater(dc.dir_x, 0.8)
        self.assertAlmostEqual(dc.dir_y, 0.0, delta=0.2)

        # DashEmitterComponent should exist and be greenish
        self.assertIn(caster, world.components['DashEmitterComponent'])
        de = world.components['DashEmitterComponent'][caster]
        self.assertGreaterEqual(de.count, 10)
        # At least one green color in palette
        greens = [(0, 255, 120), (0, 200, 80), (0, 220, 100)]
        self.assertTrue(any(tuple(c) in greens for c in de.color_choices))


if __name__ == '__main__':
    unittest.main()
