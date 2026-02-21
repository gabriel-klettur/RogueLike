from types import SimpleNamespace

from roguelike_game.ecs.components.combat.combat_stats import CombatStats
from roguelike_game.ecs.systems.combat.combat_system import CombatSystem


def test_combat_system_perform_melee_and_update_applies_damage(monkeypatch):
    t0 = 1000.0
    monkeypatch.setattr("time.time", lambda: t0)

    attacker, target = 1, 2
    world = SimpleNamespace(
        components={
            "WantsToMelee": {},
            "CombatStats": {
                attacker: CombatStats(current_hp=20, max_hp=20, power=5, defense=1),
                target: CombatStats(current_hp=15, max_hp=15, power=0, defense=3),
            },
            # weapon with damage and cooldown for attacker
            "MeleeWeapon": {attacker: SimpleNamespace(damage=2, cooldown=1.5)},
            "AttackCooldown": {},
            # mark attacker as player to enable combo queue path
            "PlayerTagComponent": {attacker: SimpleNamespace()},
        }
    )

    sys_under_test = CombatSystem()

    # Register melee intent and cooldown
    sys_under_test.perform_melee(world, attacker, target)

    # AttackCooldown set to now + cooldown
    cd = world.components["AttackCooldown"][attacker].next_time
    assert cd == t0 + 1.5

    # Resolve combat
    sys_under_test.update(world)

    # Intents cleared
    assert world.components["WantsToMelee"] == {}

    # Damage: 5 (power) + 2 (weapon) - 3 (defense) = 4
    assert world.components["CombatStats"][target].current_hp == 15 - 4

    # OnHit added to FSMEventQueue and combo event enqueued for attacker
    qmap = world.components.get("FSMEventQueue", {})
    assert target in qmap and any(ev.get("type") == "OnHit" for ev in qmap[target])
    combo_q = world.components.get("ComboEventQueue", [])
    assert any(ev.get("attacker") == attacker and ev.get("target") == target for ev in combo_q)
