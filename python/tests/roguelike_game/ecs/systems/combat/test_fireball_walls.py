import pytest
import pygame

from roguelike_game.ecs.systems.combat.spells.fireball_system import FireballSystem
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.transform.velocity import Velocity
from roguelike_game.ecs.components.abilities.fireball_component import FireballComponent
from roguelike_game.ecs.components.abilities.wall_segment_component import WallSegmentComponent


@pytest.fixture()
def sys_fb():
    return FireballSystem(perf_log=None)


def _ensure_maps(world):
    world.components.setdefault('Position', {})
    world.components.setdefault('Velocity', {})
    world.components.setdefault('FireballComponent', {})
    world.components.setdefault('WallSegmentComponent', {})


def _step_until_removed(world, sys_fb, eid, max_steps=60):
    for _ in range(max_steps):
        world.tick_frame()  # Increment frame counter so spatial hash updates
        sys_fb.update(world)
        if eid not in world.components.get('FireballComponent', {}):
            return True
    return False


def test_fireball_blocked_by_wall_obb(world, sys_fb):
    _ensure_maps(world)
    # Vertical wall segment centered at (160, 100) with 10x80
    wid = world.create_entity()
    world.components['Position'][wid] = Position(160, 100)
    world.components['WallSegmentComponent'][wid] = WallSegmentComponent(width=10, height=80, hp=100, duration=5.0, blocks_projectiles=True, blocks_units=True, angle_deg=90.0)

    # Fireball moving right from x=120 -> should intersect wall and be removed
    pid = world.create_entity()
    world.components['Position'][pid] = Position(120, 100)
    world.components['Velocity'][pid] = Velocity(30, 0)
    world.components['FireballComponent'][pid] = FireballComponent(dx=30, dy=0, damage=5, lifespan=120, caster=None, spell_key='t_wall', spawn_pos=(120, 100), hit_radius=3.0)

    removed = _step_until_removed(world, sys_fb, pid, max_steps=10)
    assert removed, "Projectile should be removed after colliding with OBB wall"


def test_fireball_passes_when_wall_does_not_block(world, sys_fb):
    _ensure_maps(world)
    # Horizontal wall that does NOT block projectiles
    wid = world.create_entity()
    world.components['Position'][wid] = Position(160, 100)
    world.components['WallSegmentComponent'][wid] = WallSegmentComponent(width=80, height=10, hp=100, duration=5.0, blocks_projectiles=False, blocks_units=True, angle_deg=0.0)

    pid = world.create_entity()
    world.components['Position'][pid] = Position(120, 100)
    world.components['Velocity'][pid] = Velocity(30, 0)
    world.components['FireballComponent'][pid] = FireballComponent(dx=30, dy=0, damage=5, lifespan=6, caster=None, spell_key='t_noblock', spawn_pos=(120, 100), hit_radius=3.0)

    # Advance a few steps; should NOT be removed by wall, only by lifespan
    for _ in range(5):
        world.tick_frame()
        sys_fb.update(world)
        assert pid in world.components.get('FireballComponent', {})
    # Next step may expire by lifespan
    world.tick_frame()
    sys_fb.update(world)
    assert pid not in world.components.get('FireballComponent', {})
