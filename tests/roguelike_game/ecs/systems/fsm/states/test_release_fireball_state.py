import pygame
import pytest

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
