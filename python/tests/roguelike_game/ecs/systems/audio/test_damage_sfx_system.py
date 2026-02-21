"""Tests for DamageSfxSystem — verifies SFX events are emitted on HP decrease."""

import unittest
from dataclasses import dataclass
from roguelike_game.ecs.systems.audio.damage_sfx_system import DamageSfxSystem
from roguelike_game.ecs.components.combat.health import Health
from roguelike_game.ecs.components.core.player_tag import PlayerTagComponent
from roguelike_game.ecs.components.monster_archetype import MonsterArchetype


class WorldStub:
    def __init__(self):
        self.components: dict = {
            "Health": {},
            "PlayerTagComponent": {},
            "MonsterArchetype": {},
            "AudioEventQueue": [],
        }


class TestDamageSfxSystem(unittest.TestCase):
    """Verify that DamageSfxSystem emits correct audio events on HP drops."""

    def _make_world(self) -> WorldStub:
        return WorldStub()

    def test_player_damage_emits_sfx(self):
        """When the player's HP decreases, a player_damage SFX event is queued."""
        world = self._make_world()
        player_eid = 1
        world.components["Health"][player_eid] = Health(current_hp=100, max_hp=100)
        world.components["PlayerTagComponent"][player_eid] = PlayerTagComponent()

        sys = DamageSfxSystem()
        # First frame: snapshot HP, no event
        sys.update(world)
        self.assertEqual(len(world.components["AudioEventQueue"]), 0)

        # Simulate damage from any source
        world.components["Health"][player_eid].current_hp = 80
        sys.update(world)

        queue = world.components["AudioEventQueue"]
        self.assertEqual(len(queue), 1)
        ev = queue[0]
        self.assertEqual(ev["type"], "play_sfx")
        self.assertTrue(ev["choices"][0].startswith("player_damage_"))
        self.assertEqual(ev["group"], "sfx")

    def test_barbol_npc_damage_emits_sfx(self):
        """When a Barbol NPC's HP decreases, a barbol_damage SFX event is queued."""
        world = self._make_world()
        npc_eid = 42
        world.components["Health"][npc_eid] = Health(current_hp=50, max_hp=50)
        world.components["MonsterArchetype"][npc_eid] = MonsterArchetype(type="barbol_oscuro")

        sys = DamageSfxSystem()
        sys.update(world)  # snapshot
        world.components["Health"][npc_eid].current_hp = 30
        sys.update(world)

        queue = world.components["AudioEventQueue"]
        self.assertEqual(len(queue), 1)
        self.assertEqual(queue[0]["choices"], ["barbol_damage_1"])

    def test_unknown_npc_no_sfx(self):
        """NPCs without a configured archetype prefix should not emit SFX."""
        world = self._make_world()
        npc_eid = 99
        world.components["Health"][npc_eid] = Health(current_hp=50, max_hp=50)
        world.components["MonsterArchetype"][npc_eid] = MonsterArchetype(type="goblin")

        sys = DamageSfxSystem()
        sys.update(world)  # snapshot
        world.components["Health"][npc_eid].current_hp = 30
        sys.update(world)

        self.assertEqual(len(world.components["AudioEventQueue"]), 0)

    def test_no_sfx_on_heal(self):
        """HP increase (healing) should NOT trigger damage SFX."""
        world = self._make_world()
        player_eid = 1
        world.components["Health"][player_eid] = Health(current_hp=50, max_hp=100)
        world.components["PlayerTagComponent"][player_eid] = PlayerTagComponent()

        sys = DamageSfxSystem()
        sys.update(world)  # snapshot
        world.components["Health"][player_eid].current_hp = 80  # healed
        sys.update(world)

        self.assertEqual(len(world.components["AudioEventQueue"]), 0)

    def test_no_sfx_on_same_hp(self):
        """No HP change should NOT trigger damage SFX."""
        world = self._make_world()
        player_eid = 1
        world.components["Health"][player_eid] = Health(current_hp=100, max_hp=100)
        world.components["PlayerTagComponent"][player_eid] = PlayerTagComponent()

        sys = DamageSfxSystem()
        sys.update(world)  # snapshot
        sys.update(world)  # same HP

        self.assertEqual(len(world.components["AudioEventQueue"]), 0)

    def test_multiple_damage_sources_same_frame(self):
        """Multiple entities damaged in the same frame each get their own SFX."""
        world = self._make_world()
        player_eid = 1
        npc_eid = 42
        world.components["Health"][player_eid] = Health(current_hp=100, max_hp=100)
        world.components["Health"][npc_eid] = Health(current_hp=50, max_hp=50)
        world.components["PlayerTagComponent"][player_eid] = PlayerTagComponent()
        world.components["MonsterArchetype"][npc_eid] = MonsterArchetype(type="barbol_boss")

        sys = DamageSfxSystem()
        sys.update(world)  # snapshot

        world.components["Health"][player_eid].current_hp = 70
        world.components["Health"][npc_eid].current_hp = 20
        sys.update(world)

        queue = world.components["AudioEventQueue"]
        self.assertEqual(len(queue), 2)

    def test_pruned_entities_do_not_leak(self):
        """Entities removed from the Health map should be pruned from internal state."""
        world = self._make_world()
        eid = 10
        world.components["Health"][eid] = Health(current_hp=100, max_hp=100)

        sys = DamageSfxSystem()
        sys.update(world)
        self.assertIn(eid, sys._prev_hp)

        del world.components["Health"][eid]
        sys.update(world)
        self.assertNotIn(eid, sys._prev_hp)


if __name__ == "__main__":
    unittest.main()
