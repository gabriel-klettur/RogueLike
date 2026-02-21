import types
import pygame
import pytest

from roguelike_game.ecs.systems.combat.spells.fireball_system.collisions.units import apply_combat_effects, CollisionResult
from roguelike_game.ecs.systems.combat.spells.fireball_system.runtime import FireballRuntime
from roguelike_game.ecs.systems.combat.hitbox_system import HitboxSystem


class _Component:
    def __init__(self, **kw):
        for k, v in kw.items():
            setattr(self, k, v)


class _Config:
    pass


def _mk_runtime(world, caster: int, damage: int) -> FireballRuntime:
    # Build a minimal FireballRuntime-like object
    rt = types.SimpleNamespace()
    rt.world = world
    rt.component = _Component(caster=caster, damage=damage)
    rt.config = _Config()
    rt.entity_id = world.create_entity()
    return rt  # type: ignore


def test_fireball_injection_adds_white_flash(world, monkeypatch):
    # Arrange runtime and target
    caster = world.create_entity()
    target = world.create_entity()
    world.components.setdefault("Health", {})[target] = types.SimpleNamespace(current_hp=20)

    # Positions to satisfy _push_fsm_events
    world.components.setdefault("Position", {})[caster] = types.SimpleNamespace(x=0.0, y=0.0)
    world.components.setdefault("Position", {})[target] = types.SimpleNamespace(x=1.0, y=0.0)

    # Avoid side effects: patch helpers used by apply_combat_effects
    import roguelike_game.ecs.systems.combat.spells.fireball_system.collisions.units as units
    monkeypatch.setattr(units, "spawn_impact_effects", lambda *a, **k: None)
    monkeypatch.setattr(units, "get_scale_multiplier", lambda *_a, **_k: 1.0)
    monkeypatch.setattr(units, "_push_fsm_events", lambda *a, **k: None)

    runtime = _mk_runtime(world, caster=caster, damage=3)
    coll = CollisionResult(target, (0.0, 0.0), "point")

    # Act
    apply_combat_effects(runtime, coll)

    # Assert white flash component present
    flashes = world.components.get("FlashComponent", {})
    assert target in flashes, "Fireball impact should add a short white FlashComponent to the target"


def test_hitbox_injection_adds_white_flash(world, camera):
    # Minimal target with health
    target = world.create_entity()
    world.components.setdefault("Health", {})[target] = types.SimpleNamespace(current_hp=50)

    # Hitbox entity positioned near target
    hb_eid = world.create_entity()
    world.components.setdefault("Position", {})[hb_eid] = types.SimpleNamespace(x=0.0, y=0.0)
    world.components.setdefault("Position", {})[target] = types.SimpleNamespace(x=5.0, y=0.0)

    # Minimal HitboxComponent-like object
    hb = _Component(
        owner=hb_eid,
        direction=(1.0, 0.0),
        arc_angle=6.28318530718,  # 2*pi: full circle
        radius=32.0,
        damage=7,
        lifespan=2,
        hit_targets=set(),
        follow_owner=False,
        rotate_with_owner=False,
        offset=0.0,
        status=None,
        element="",
    )
    world.components.setdefault("HitboxComponent", {})[hb_eid] = hb

    # Act
    HitboxSystem().update(world, camera)

    # Assert: damage applied and flash component added
    assert world.components["Health"][target].current_hp == 43
    flashes = world.components.get("FlashComponent", {})
    assert target in flashes, "Hitbox damage should add a short white FlashComponent to the target"
