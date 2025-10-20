import math
import unittest

import pygame

from roguelike_game.ecs.systems.combat.hitbox_system import HitboxSystem
from roguelike_game.ecs.components.combat.hitbox import HitboxComponent
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.combat.health import Health
from roguelike_game.ecs.components.core.identity import Identity, Faction
from roguelike_game.ecs.components.combat.allow_friendly_fire import AllowFriendlyFire


class FakeCamera:
    def __init__(self):
        self.zoom = 1.0
        self.offset_x = 0.0
        self.offset_y = 0.0

    def apply(self, pos):
        # Identity transform
        return pos


class WorldStub:
    def __init__(self):
        self.components = {
            'Position': {},
            'HitboxComponent': {},
            'Health': {},
            'Identity': {},
            'MonsterArchetype': {},
        }
        self._next = 1

    def create_entity(self):
        eid = self._next
        self._next += 1
        return eid

    def remove_entity(self, eid):
        for comp in self.components.values():
            comp.pop(eid, None)


class TestFriendlyFireHitbox(unittest.TestCase):
    def setUp(self):
        # Ensure pygame is initialized enough for Surface/mask
        if not pygame.get_init():
            pygame.init()

    def test_same_faction_monsters_no_damage(self):
        world = WorldStub()
        cam = FakeCamera()
        sys = HitboxSystem(perf_log=None)

        # Owner (monster A)
        owner = world.create_entity()
        world.components['MonsterArchetype'][owner] = object()
        world.components['Identity'][owner] = Identity(id=owner, name='A', title='', faction=Faction.EVIL)
        world.components['Position'][owner] = Position(100.0, 100.0)
        world.components['Health'][owner] = Health(current_hp=100, max_hp=100)

        # Target (monster B)
        target = world.create_entity()
        world.components['MonsterArchetype'][target] = object()
        world.components['Identity'][target] = Identity(id=target, name='B', title='', faction=Faction.EVIL)
        world.components['Position'][target] = Position(140.0, 100.0)  # 40px to the right
        world.components['Health'][target] = Health(current_hp=100, max_hp=100)

        # Hitbox entity centered ahead of owner, radius covers target, arc faces +X
        hb = world.create_entity()
        world.components['Position'][hb] = Position(110.0, 100.0)
        world.components['HitboxComponent'][hb] = HitboxComponent(
            owner=owner,
            offset=10.0,
            radius=60.0,
            arc_angle=math.radians(90.0),
            direction=(1.0, 0.0),
            lifespan=3,
            damage=25.0,
            follow_owner=False,
            rotate_with_owner=False,
        )

        # Update once: target should be within arc, but no damage because same faction
        sys.update(world, camera=cam)
        self.assertEqual(world.components['Health'][target].current_hp, 100)
        # Ensure hit was recorded as processed to avoid re-hits this lifespan
        self.assertIn(target, world.components['HitboxComponent'][hb].hit_targets)

    def test_allow_friendly_fire_bypasses_filter(self):
        world = WorldStub()
        cam = FakeCamera()
        sys = HitboxSystem(perf_log=None)

        owner = world.create_entity()
        world.components['MonsterArchetype'][owner] = object()
        world.components['Identity'][owner] = Identity(id=owner, name='A', title='', faction=Faction.EVIL)
        world.components['Position'][owner] = Position(100.0, 100.0)
        world.components['Health'][owner] = Health(current_hp=100, max_hp=100)
        world.components.setdefault('AllowFriendlyFire', {})[owner] = AllowFriendlyFire(enabled=True)

        target = world.create_entity()
        world.components['MonsterArchetype'][target] = object()
        world.components['Identity'][target] = Identity(id=target, name='B', title='', faction=Faction.EVIL)
        world.components['Position'][target] = Position(140.0, 100.0)
        world.components['Health'][target] = Health(current_hp=100, max_hp=100)

        hb = world.create_entity()
        world.components['Position'][hb] = Position(110.0, 100.0)
        world.components['HitboxComponent'][hb] = HitboxComponent(
            owner=owner,
            offset=10.0,
            radius=60.0,
            arc_angle=math.radians(90.0),
            direction=(1.0, 0.0),
            lifespan=3,
            damage=25.0,
            follow_owner=False,
            rotate_with_owner=False,
        )

        sys.update(world, camera=cam)
        self.assertEqual(world.components['Health'][target].current_hp, 75)


if __name__ == '__main__':
    unittest.main()
