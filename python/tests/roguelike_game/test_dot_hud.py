import types
import time
import pytest

from roguelike_game.ecs.systems.status.dot_system import DoTSystem
from roguelike_game.ecs.components.status.poison_component import PoisonComponent


def _set_time(monkeypatch, t: float):
    monkeypatch.setattr(time, "time", lambda: t)


def test_dot_updates_target_hud_when_applier_is_player(world, monkeypatch):
    player = world.create_entity()
    target = world.create_entity()
    world.player_entity = player
    world.components.setdefault("Health", {})[target] = types.SimpleNamespace(current_hp=30)

    t0 = 10_000.0
    world.components.setdefault("PoisonComponent", {})[target] = PoisonComponent(
        damage_per_tick=1,
        duration=1.0,
        tick_period=0.5,
        start_time=t0,
        last_tick_time=t0,
        applier=player,
    )

    sys = DoTSystem()

    # First tick -> should update HUD
    _set_time(monkeypatch, t0 + 0.5)
    sys.update(world)

    hud = world.components.get("TargetHUD", {})
    assert hud.get("target_eid") == int(target)
    assert isinstance(hud.get("last_hit_time", 0.0), float)
    assert hud.get("ttl_s") == 3.0
