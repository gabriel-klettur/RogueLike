from types import SimpleNamespace

from roguelike_game.ecs.systems.combat.combat_system import CombatSystem


def test_combat_update_many_iterations_does_not_block():
    world = SimpleNamespace(components={
        "WantsToMelee": {},
        "CombatStats": {},
        "MeleeWeapon": {},
    })
    sys_under_test = CombatSystem()

    # Run many iterations; update should be safe and non-blocking
    for _ in range(200):
        sys_under_test.update(world)
