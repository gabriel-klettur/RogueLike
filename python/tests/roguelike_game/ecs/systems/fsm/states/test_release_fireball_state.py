import pygame
import pytest
import math

from roguelike_game.ecs.systems.fsm.states.spell.release_spell_state import ReleaseSpellState
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.transform.velocity import Velocity
from roguelike_game.ecs.components.abilities.fireball_component import FireballComponent


class _Entity:
    def __init__(self, world, eid):
        self.world = world
        self.id = eid


class _DummyFSM:
    def __init__(self, ctx):
        self.context = ctx

    def change_state(self, *_a, **_k):
        pass


def _ensure_maps(world):
    world.components.setdefault('Position', {})
    world.components.setdefault('Velocity', {})
    world.components.setdefault('FireballComponent', {})
    world.components.setdefault('Sprite', {})
    world.components.setdefault('Scale', {})


@pytest.fixture()
def release_state():
    rs = ReleaseSpellState()
    return rs


def test_parallel_shot_spawns_three(world, monkeypatch, release_state):
    _ensure_maps(world)
    # Caster entity at origin
    caster = world.create_entity()
    world.components['Position'][caster] = Position(0, 0)

    # Avoid file IO for sprite load
    monkeypatch.setattr(pygame.image, 'load', lambda p: pygame.Surface((8, 8), pygame.SRCALPHA))

    # Prepare context for fireball projectile with parallel_count=3
    ctx = {
        'spell': 'fireball',
        'direction': (1.0, 0.0),
        'scale_multiplier': 1.0,
        'hit_radius': 3.0,
        'hit_radius_multiplier': 1.0,
        'parallel_count': 3,
        'parallel_spacing': 48.0,
        'central_forward_offset': 24.0,
        'sides_forward_offset': 12.0,
    }
    ent = _Entity(world, caster)
    release_state.fsm = _DummyFSM(ctx)

    release_state.enter(ent)

    # Expect 3 fireballs spawned with same velocity and different positions
    fbs = list(world.components['FireballComponent'].items())
    assert len(fbs) == 3, f"expected 3 projectiles, got {len(fbs)}"
    vels = [world.components['Velocity'][eid] for eid, _ in fbs]
    for v in vels:
        # vx must be positive (projectiles fired along +x)
        assert v.vx > 0
        # vy should be zero within tolerance
        assert v.vy == pytest.approx(0.0, abs=1e-6)


def test_radial_burst_count(world, monkeypatch, release_state):
    _ensure_maps(world)
    caster = world.create_entity()
    world.components['Position'][caster] = Position(50, 50)
    monkeypatch.setattr(pygame.image, 'load', lambda p: pygame.Surface((8, 8), pygame.SRCALPHA))

    ctx = {
        'spell': 'fireball',
        'direction': (1.0, 0.0),
        'radial_count': 8,
        'radial_start_deg': 45.0,
        'central_forward_offset': 0.0,
        'scale_multiplier': 1.0,
        'hit_radius': 2.0,
    }
    ent = _Entity(world, caster)
    release_state.fsm = _DummyFSM(ctx)

    release_state.enter(ent)

    fbs = world.components['FireballComponent']
    assert len(fbs) == 8
    # Directions should be distributed (verify diverse velocities)
    vset = {(round(world.components['Velocity'][eid].vx, 3), round(world.components['Velocity'][eid].vy, 3)) for eid in fbs}
    assert len(vset) == 8


def test_fireball_unlocked_reaims_each_release(world, monkeypatch, release_state):
    _ensure_maps(world)
    caster = world.create_entity()
    world.components['Position'][caster] = Position(100, 100)

    monkeypatch.setattr(pygame.image, 'load', lambda p: pygame.Surface((8, 8), pygame.SRCALPHA))

    mouse = {'pos': (130, 100)}
    monkeypatch.setattr(pygame.mouse, 'get_pos', lambda: mouse['pos'])

    ctx = {
        'spell': 'fireball',
        'direction': (-1.0, 0.0),
    }
    ent = _Entity(world, caster)
    release_state.fsm = _DummyFSM(ctx)

    release_state.enter(ent)
    eid1 = max(world.components['FireballComponent'].keys())
    v1 = world.components['Velocity'][eid1]
    n1 = (v1.vx / (math.hypot(v1.vx, v1.vy) or 1.0), v1.vy / (math.hypot(v1.vx, v1.vy) or 1.0))
    assert n1[0] == pytest.approx(1.0, abs=1e-6)
    assert n1[1] == pytest.approx(0.0, abs=1e-6)

    mouse['pos'] = (100, 140)
    release_state.enter(ent)
    eid2 = max(world.components['FireballComponent'].keys())
    v2 = world.components['Velocity'][eid2]
    n2 = (v2.vx / (math.hypot(v2.vx, v2.vy) or 1.0), v2.vy / (math.hypot(v2.vx, v2.vy) or 1.0))
    assert n2[0] == pytest.approx(0.0, abs=1e-6)
    assert n2[1] == pytest.approx(1.0, abs=1e-6)


def test_force_lock_direction_ignores_mouse(world, monkeypatch, release_state):
    _ensure_maps(world)
    caster = world.create_entity()
    world.components['Position'][caster] = Position(100, 100)

    monkeypatch.setattr(pygame.image, 'load', lambda p: pygame.Surface((8, 8), pygame.SRCALPHA))

    monkeypatch.setattr(pygame.mouse, 'get_pos', lambda: (200, 200))

    ctx = {
        'spell': 'fireball',
        'direction': (0.0, -1.0),
        'force_lock_direction': True,
    }
    ent = _Entity(world, caster)
    release_state.fsm = _DummyFSM(ctx)

    release_state.enter(ent)
    eid = max(world.components['FireballComponent'].keys())
    v = world.components['Velocity'][eid]
    n = (v.vx / (math.hypot(v.vx, v.vy) or 1.0), v.vy / (math.hypot(v.vx, v.vy) or 1.0))
    assert n[0] == pytest.approx(0.0, abs=1e-6)
    assert n[1] == pytest.approx(-1.0, abs=1e-6)


def test_unlocked_uses_camera_transform(world, camera, monkeypatch, release_state):
    _ensure_maps(world)
    caster = world.create_entity()
    world.components['Position'][caster] = Position(100, 100)

    monkeypatch.setattr(pygame.image, 'load', lambda p: pygame.Surface((8, 8), pygame.SRCALPHA))

    camera.zoom = 2.0
    camera.offset_x = 10.0
    camera.offset_y = 20.0

    mouse = {'pos': (200, 160)}
    monkeypatch.setattr(pygame.mouse, 'get_pos', lambda: mouse['pos'])

    ctx = {
        'spell': 'fireball',
        'direction': (-1.0, 0.0),
        'camera': camera,
    }
    ent = _Entity(world, caster)
    release_state.fsm = _DummyFSM(ctx)

    release_state.enter(ent)
    eid = max(world.components['FireballComponent'].keys())
    v = world.components['Velocity'][eid]
    n = (v.vx / (math.hypot(v.vx, v.vy) or 1.0), v.vy / (math.hypot(v.vx, v.vy) or 1.0))
    assert n[0] == pytest.approx(1.0, abs=1e-6)
    assert n[1] == pytest.approx(0.0, abs=1e-6)
