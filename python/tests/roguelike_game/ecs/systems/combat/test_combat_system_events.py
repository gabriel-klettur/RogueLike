from types import SimpleNamespace

from roguelike_game.ecs.components.combat.combat_stats import CombatStats
from roguelike_game.ecs.systems.combat.combat_system import CombatSystem


def test_combat_system_emits_onhit_ondeath_and_combo_events(monkeypatch):
    t0 = 1000.0
    monkeypatch.setattr("time.time", lambda: t0)

    attacker, target = 1, 2
    world = SimpleNamespace(
        components={
            "WantsToMelee": {},
            "CombatStats": {
                attacker: CombatStats(current_hp=20, max_hp=20, power=5, defense=1),
                target: CombatStats(current_hp=5, max_hp=5, power=0, defense=0),
            },
            # weapon strong enough to guarantee kill
            "MeleeWeapon": {attacker: SimpleNamespace(damage=5, cooldown=0.5)},
            "AttackCooldown": {},
            # mark attacker as player to enable combo paths
            "PlayerTagComponent": {attacker: SimpleNamespace()},
            # optional position to allow from_left computation
            "Position": {
                attacker: SimpleNamespace(x=0, y=0),
                target: SimpleNamespace(x=10, y=0),
            },
            # sprite/scale maps may be empty; fallback branch should handle
            "Sprite": {},
            "Scale": {},
        }
    )

    sys_under_test = CombatSystem()
    sys_under_test.perform_melee(world, attacker, target)
    sys_under_test.update(world)

    # OnHit + OnDeath en cola FSM del objetivo
    qmap = world.components.get("FSMEventQueue", {})
    q = qmap.get(target, [])
    types = [ev.get("type") for ev in q]
    assert "OnHit" in types and "OnDeath" in types

    # ComboEventQueue contiene 'kill' y el evento de daño del atacante
    combo_q = world.components.get("ComboEventQueue", [])
    kinds = [ev.get("type") for ev in combo_q if ev.get("type")]
    assert "kill" in kinds or any(ev.get("entity") == attacker and ev.get("target") == target for ev in combo_q)
