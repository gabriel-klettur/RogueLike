from types import SimpleNamespace

from roguelike_game.ecs.systems.combat.combat_system import CombatSystem


def test_combat_update_with_no_events_is_noop():
    # World without any combat-related maps beyond the required queue
    world = SimpleNamespace(components={
        "WantsToMelee": {},  # empty -> update should be a no-op
        # Optional maps intentionally omitted
        "CombatStats": {},
        "MeleeWeapon": {},
    })
    sys_under_test = CombatSystem()
    # Should not raise
    sys_under_test.update(world)
