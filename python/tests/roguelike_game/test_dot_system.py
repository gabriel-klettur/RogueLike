import time
import types
import pygame
import pytest

from roguelike_game.ecs.systems.status.dot_system import DoTSystem
from roguelike_game.ecs.components.combat.burn import BurnComponent
from roguelike_game.ecs.components.status.poison_component import PoisonComponent
from roguelike_game.ecs.components.status.stun_component import StunComponent
from roguelike_game.ecs.systems.rendering.flash_system import FlashSystem


class _SpriteStub:
    def __init__(self, w: int = 2, h: int = 2, color=(0, 0, 0)):
        self.image = pygame.Surface((w, h), pygame.SRCALPHA)
        self.image.fill(color)


class _Pos:
    def __init__(self, x: float, y: float):
        self.x = x
        self.y = y


@pytest.fixture()
def eid(world):
    return world.create_entity()


def _set_time(monkeypatch, t: float):
    monkeypatch.setattr(time, "time", lambda: t)


def test_dot_ticks_and_expires_burn_and_poison(world, monkeypatch):
    eid = world.create_entity()
    world.components.setdefault("Health", {})[eid] = types.SimpleNamespace(current_hp=100)

    t0 = 1_000.0
    world.components.setdefault("BurnComponent", {})[eid] = BurnComponent(
        damage_per_tick=5, duration=2.0, tick_period=0.5, start_time=t0, last_tick_time=t0, applier=None
    )
    world.components.setdefault("PoisonComponent", {})[eid] = PoisonComponent(
        damage_per_tick=2, duration=1.0, tick_period=0.5, start_time=t0, last_tick_time=t0, applier=None
    )

    sys = DoTSystem()

    # First tick at t0+0.5: burn -5, poison -2
    _set_time(monkeypatch, t0 + 0.5)
    sys.update(world)
    assert world.components["Health"][eid].current_hp == 93

    # Jump to t0+1.6: burn ticks at 1.0 and 1.5 (-10), poison ticks at 1.0 and 1.5 but expires at 1.0+1.0 -> only 1.0 and 1.5? Max tick time min(now, end_time)=t0+1.0 -> exactly one more tick (-2)
    _set_time(monkeypatch, t0 + 1.6)
    sys.update(world)
    # After this call: HP 93 -10 (burn) -2 (poison) = 81
    assert world.components["Health"][eid].current_hp == 81

    # Expire both at/after end
    _set_time(monkeypatch, t0 + 2.1)
    sys.update(world)
    assert eid not in world.components.get("BurnComponent", {})
    # Poison duration was 1.0, should already be gone
    assert eid not in world.components.get("PoisonComponent", {})


def test_dot_skips_dead_dying_and_neutral(world, monkeypatch):
    eid = world.create_entity()
    world.components.setdefault("Health", {})[eid] = types.SimpleNamespace(current_hp=50)
    t0 = 2_000.0
    world.components.setdefault("BurnComponent", {})[eid] = BurnComponent(
        damage_per_tick=5, duration=2.0, tick_period=0.5, start_time=t0, last_tick_time=t0, applier=None
    )

    # Mark as neutral by monkeypatching is_neutral
    import roguelike_game.ecs.utils.health_utils as hu

    monkeypatch.setattr(hu, "is_neutral", lambda _w, _eid: True)

    sys = DoTSystem()
    _set_time(monkeypatch, t0 + 1.0)
    sys.update(world)
    # No damage applied
    assert world.components["Health"][eid].current_hp == 50

    # Dead entities: ensure skip
    world.components.setdefault("DeathTimer", {})[eid] = types.SimpleNamespace(remaining=1.0)
    _set_time(monkeypatch, t0 + 1.5)
    sys.update(world)
    assert world.components["Health"][eid].current_hp == 50


def test_flash_priority_stun_over_burn_over_poison_over_hit(world, monkeypatch):
    eid = world.create_entity()
    # Minimal sprite and position for FlashSystem
    world.components.setdefault("Sprite", {})[eid] = _SpriteStub()
    world.components.setdefault("Position", {})[eid] = _Pos(0, 0)

    fs = FlashSystem()

    base_t = 3_000.0
    _set_time(monkeypatch, base_t)

    # Add all statuses + hit flash
    world.components.setdefault("StunComponent", {})[eid] = StunComponent.create(duration=1.0)
    world.components.setdefault("BurnComponent", {})[eid] = BurnComponent(
        damage_per_tick=1, duration=2.0, tick_period=1.0, start_time=base_t, last_tick_time=base_t, applier=None
    )
    world.components.setdefault("PoisonComponent", {})[eid] = PoisonComponent(
        damage_per_tick=1, duration=2.0, tick_period=1.0, start_time=base_t, last_tick_time=base_t, applier=None
    )
    world.components.setdefault("FlashComponent", {})[eid] = types.SimpleNamespace(color=(255, 255, 255), duration=0.5, start_time=base_t)

    fs.update(world)
    px = world.components["Sprite"][eid].image.get_at((0, 0))
    # Stun -> yellow
    assert (px.r, px.g, px.b) == (255, 255, 0)

    # Remove stun -> Burn should take precedence => red-ish (255,64,64)
    del world.components["StunComponent"][eid]
    fs.update(world)
    px = world.components["Sprite"][eid].image.get_at((0, 0))
    assert (px.r, px.g, px.b) == (255, 64, 64)

    # Remove burn -> Poison => green-ish (64,255,64)
    del world.components["BurnComponent"][eid]
    fs.update(world)
    px = world.components["Sprite"][eid].image.get_at((0, 0))
    assert (px.r, px.g, px.b) == (64, 255, 64)

    # Remove poison -> falls back to hit flash => white
    del world.components["PoisonComponent"][eid]
    fs.update(world)
    px = world.components["Sprite"][eid].image.get_at((0, 0))
    assert (px.r, px.g, px.b) == (255, 255, 255)

    # Advance beyond hit duration -> Animator would reset base frame each frame.
    # Simulate by clearing the sprite to black before calling FlashSystem again.
    _set_time(monkeypatch, base_t + 1.0)
    world.components["Sprite"][eid].image.fill((0, 0, 0))
    fs.update(world)
    px = world.components["Sprite"][eid].image.get_at((0, 0))
    assert (px.r, px.g, px.b) == (0, 0, 0)
