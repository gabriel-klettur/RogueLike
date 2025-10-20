import unittest

from roguelike_game.ecs.systems.fsm.states.attack_state import AttackState
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.combat.health import Health
from roguelike_game.ecs.components.combat.melee_range import MeleeRange
from roguelike_game.ecs.components.monster_archetype import MonsterArchetype


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
            'MonsterArchetype': {},
            'HitboxComponent': {},
            'SlashEmitterComponent': {},
            'NPCAttackCooldown': {},
        }
        self.player_entity = None


class TestPerClassSlashColors(unittest.TestCase):
    def _setup_and_execute(self, mtype: str):
        world = WorldStub()
        npc = 100
        player = 200
        world.player_entity = player
        world.components['Position'][npc] = Position(100.0, 100.0)
        world.components['Health'][npc] = Health(current_hp=100, max_hp=100)
        world.components['MeleeRange'][npc] = MeleeRange(range=5)
        world.components['MonsterArchetype'][npc] = MonsterArchetype(type=mtype)
        world.components['Position'][player] = Position(150.0, 100.0)
        world.components['Health'][player] = Health(current_hp=100, max_hp=100)

        state = AttackState()
        proxy = EntityProxy(world, npc)
        state.execute(proxy, dt=0.016)
        em = world.components['SlashEmitterComponent'][npc]
        return world, em

    def test_barbol_oscuro_black(self):
        _, em = self._setup_and_execute('barbol_oscuro')
        self.assertEqual(tuple(em.color), (10, 10, 10))
        self.assertGreaterEqual(em.radius, 30)

    def test_barbol_morado_purple(self):
        _, em = self._setup_and_execute('barbol_morado')
        self.assertEqual(tuple(em.color), (128, 0, 128))
        self.assertGreaterEqual(em.radius, 34)

    def test_barbol_boss_red(self):
        _, em = self._setup_and_execute('barbol_boss')
        self.assertEqual(tuple(em.color), (255, 0, 0))
        self.assertGreaterEqual(em.radius, 48)

    def test_barbol_cyan_cyan(self):
        _, em = self._setup_and_execute('barbol_cyan')
        self.assertEqual(tuple(em.color), (0, 255, 255))
        self.assertGreaterEqual(em.radius, 32)

    def test_barbol_gris_gray(self):
        _, em = self._setup_and_execute('barbol_gris')
        self.assertEqual(tuple(em.color), (150, 150, 150))
        self.assertGreaterEqual(em.radius, 30)


if __name__ == '__main__':
    unittest.main()
