import types
import time as _time
import pygame
import pytest

from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.combat.health import Health
from roguelike_game.ecs.components.physics.collider import Collider
from roguelike_game.ecs.components.abilities.puddle_component import PuddleComponent
from roguelike_game.ecs.systems.combat.spells.puddle_system import PuddleSystem
from roguelike_game.ecs.systems.combat.burn_system import BurnSystem
from roguelike_game.ecs.components.rendering.sprite import Sprite


class _World:
    def __init__(self):
        self.components = {
            'Position': {},
            'Health': {},
            'PuddleComponent': {},
            'Sprite': {},
            'Scale': {},
            'Collider': {},
        }
        self.player_entity = 1
        self._next = 100

    def create_entity(self):
        eid = self._next
        self._next += 1
        return eid


def test_puddle_applies_burn_on_edge_contact(monkeypatch):
    # Deterministic time for puddle ticks
    t0 = 1_000.0
    t1 = t0 + 0.2
    times = [t0, t1]
    monkeypatch.setattr('time.time', lambda: times.pop(0))

    world = _World()
    # Create puddle centered at (100, 100)
    pid = world.create_entity()
    world.components['Position'][pid] = Position(100, 100)
    puddle = PuddleComponent(
        radius=20,
        duration=3.0,
        tick_period=0.1,
        damage=0,
        heal=0,
        status={'burn': {'dps': 5, 'duration': 3.0, 'tick_period': 1.0}},
        move_speed_mult=1.0,
        element='lava',
        color=(255, 120, 60),
        alpha=96,
        owner=world.player_entity,
        spell_key='puddle_lava',
    )
    world.components['PuddleComponent'][pid] = puddle

    # Target entity positioned so that its edge just touches the puddle edge
    # We estimate entity radius from its Collider (width=height=10 => r=5)
    tid = 200
    world.components['Position'][tid] = Position(100 + 25 - 0.5, 100)  # center distance ~24.5 < 25
    world.components['Health'][tid] = Health(current_hp=10, max_hp=10)
    world.components['Collider'][tid] = Collider(width=10, height=10)

    sys = PuddleSystem()
    sys.update(world)

    burns = world.components.get('BurnComponent', {})
    assert tid in burns, 'BurnComponent should be applied when touching puddle edge'
    bc = burns[tid]
    assert int(getattr(bc, 'damage_per_tick', 0)) == 5
    assert pytest.approx(getattr(bc, 'duration', 0.0), abs=1e-6) == 3.0
    assert pytest.approx(getattr(bc, 'tick_period', 0.0), abs=1e-6) == 1.0


def test_puddle_expires_and_cleans_components(monkeypatch):
    # Time: create at t0, update beyond duration
    t0 = 2_000.0
    t1 = t0 + 5.0
    times = [t0, t1]
    monkeypatch.setattr('time.time', lambda: times.pop(0))

    world = _World()
    pid = world.create_entity()
    world.components['Position'][pid] = Position(50, 50)
    world.components['Sprite'][pid] = types.SimpleNamespace(image=pygame.Surface((10, 10)))
    world.components['Scale'][pid] = types.SimpleNamespace(scale=1.0)
    world.components['PuddleComponent'][pid] = PuddleComponent(
        radius=10, duration=0.1, tick_period=0.05, element='lava', color=(255, 120, 60), alpha=90
    )

    PuddleSystem().update(world)

    assert pid not in world.components['PuddleComponent']
    assert pid not in world.components['Sprite']
    assert pid not in world.components['Scale']
    assert pid not in world.components['Position']


def test_burn_system_ticks_damage_and_marks_target_hud(monkeypatch):
    # t0: apply; t1: 1s later -> one tick
    t0 = 3_000.0
    t1 = t0 + 1.05
    t2 = t0 + 3.1  # beyond duration -> removed
    times = [t0, t1, t2]
    monkeypatch.setattr('time.time', lambda: times.pop(0))

    world = _World()
    target = 300
    world.components['Position'][target] = Position(0, 0)
    world.components['Health'][target] = Health(current_hp=10, max_hp=10)
    world.components['BurnComponent'] = {
        target: types.SimpleNamespace(
            damage_per_tick=5, duration=3.0, tick_period=1.0, start_time=_time.time(), last_tick_time=_time.time(), applier=world.player_entity
        )
    }

    sys = BurnSystem()
    # First tick applies 5 damage and sets TargetHUD
    sys.update(world)
    assert world.components['Health'][target].current_hp == 5
    hud = world.components.get('TargetHUD', {})
    assert hud.get('target_eid') == target
    assert 'last_hit_time' in hud
    # Next update beyond duration removes burn
    sys.update(world)
    assert target not in world.components.get('BurnComponent', {})


def test_puddle_uses_sprite_radius_and_scale(monkeypatch):
    # time: allow immediate tick
    t0 = 4_000.0
    t1 = t0 + 0.2
    times = [t0, t1]
    monkeypatch.setattr('time.time', lambda: times.pop(0))

    world = _World()
    pid = world.create_entity()
    world.components['Position'][pid] = Position(200, 200)
    world.components['PuddleComponent'][pid] = PuddleComponent(
        radius=20, duration=1.0, tick_period=0.1, element='lava'
    )

    # Target with a 30x10 sprite scaled by 2.0 -> radius ~ (max(30,10)/2)*2 = 30
    tid = 201
    world.components['Position'][tid] = Position(200 + 20 + 30 - 1, 200)  # just within contact
    world.components['Health'][tid] = Health(current_hp=5, max_hp=5)
    world.components['Sprite'][tid] = Sprite(pygame.Surface((30, 10), pygame.SRCALPHA))
    world.components['Scale'][tid] = types.SimpleNamespace(scale=2.0)

    PuddleSystem().update(world)
    assert tid in world.components.get('BurnComponent', {})


def test_puddle_respects_tick_period_no_reapply_before_tick(monkeypatch):
    t0 = 5_000.0
    # First update at t0, second at t0+0.01 while tick_period=0.1 -> second should skip
    t1 = t0 + 0.0
    t2 = t0 + 0.01
    times = [t0, t1, t2]
    monkeypatch.setattr('time.time', lambda: times.pop(0))

    world = _World()
    pid = world.create_entity()
    world.components['Position'][pid] = Position(300, 300)
    world.components['PuddleComponent'][pid] = PuddleComponent(
        radius=10, duration=2.0, tick_period=0.1, element='lava'
    )
    tid = 301
    world.components['Position'][tid] = Position(300, 300)
    world.components['Health'][tid] = Health(current_hp=10, max_hp=10)

    sys = PuddleSystem()
    sys.update(world)  # apply burn first time
    assert tid in world.components.get('BurnComponent', {})
    # Second update happens before tick_period, should not modify last_tick_time nor add new burns
    before = world.components['PuddleComponent'][pid].last_tick_time
    sys.update(world)
    after = world.components['PuddleComponent'][pid].last_tick_time
    assert after == before


def test_puddle_does_not_burn_owner(monkeypatch):
    t0 = 6_000.0
    t1 = t0 + 0.2
    times = [t0, t1]
    monkeypatch.setattr('time.time', lambda: times.pop(0))

    world = _World()
    owner = world.player_entity
    pid = world.create_entity()
    world.components['Position'][pid] = Position(400, 400)
    world.components['PuddleComponent'][pid] = PuddleComponent(
        radius=15, duration=1.0, tick_period=0.1, element='lava', owner=owner
    )

    # Owner standing in the puddle center
    world.components['Position'][owner] = Position(400, 400)
    world.components['Health'][owner] = Health(current_hp=10, max_hp=10)

    PuddleSystem().update(world)
    burns = world.components.get('BurnComponent', {})
    assert owner not in burns


def test_non_lava_puddle_does_not_apply_burn(monkeypatch):
    t0 = 7_000.0
    t1 = t0 + 0.2
    times = [t0, t1]
    monkeypatch.setattr('time.time', lambda: times.pop(0))

    world = _World()
    pid = world.create_entity()
    world.components['Position'][pid] = Position(500, 500)
    world.components['PuddleComponent'][pid] = PuddleComponent(
        radius=12, duration=1.0, tick_period=0.1, element='water'
    )
    tid = 700
    world.components['Position'][tid] = Position(500, 500)
    world.components['Health'][tid] = Health(current_hp=5, max_hp=5)

    PuddleSystem().update(world)
    assert tid not in world.components.get('BurnComponent', {})
