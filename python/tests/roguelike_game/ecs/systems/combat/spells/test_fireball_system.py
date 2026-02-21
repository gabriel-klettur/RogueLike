import pygame
import pytest

from roguelike_game.ecs.systems.combat.spells.fireball_system import FireballSystem
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.transform.velocity import Velocity
from roguelike_game.ecs.components.abilities.fireball_component import FireballComponent
from roguelike_game.ecs.components.physics.multi_collider import MultiCollider
from roguelike_game.ecs.components.physics.collider import Collider
from roguelike_game.ecs.components.physics.mask_collider import MaskCollider
from roguelike_game.ecs.components.combat.health import Health
from roguelike_game.ecs.components.core.identity import Identity, Faction
import roguelike_game.ecs.systems.combat.spells.fireball_system as fireball_system_mod


@pytest.fixture()
def sys_fb():
    return FireballSystem(perf_log=None)


def _ensure_maps(world):
    world.components.setdefault('Position', {})
    world.components.setdefault('Velocity', {})
    world.components.setdefault('FireballComponent', {})
    world.components.setdefault('MultiCollider', {})
    world.components.setdefault('Health', {})
    world.components.setdefault('Identity', {})


def _step_until_removed(world, sys_fb, eid, max_steps=120):
    for _ in range(max_steps):
        world.tick_frame()  # Increment frame counter so spatial hash updates
        sys_fb.update(world)
        if eid not in world.components.get('FireballComponent', {}):
            return True
    return False


def test_fireball_hits_rect_collider_with_sampling(world, sys_fb):
    _ensure_maps(world)
    # Target with rectangular collider at (200, 100)
    tid = world.create_entity()
    world.components['Position'][tid] = Position(200, 100)
    body = Collider(width=20, height=20, offset_x=-10, offset_y=-10)
    world.components['MultiCollider'][tid] = MultiCollider({'body': body})
    world.components['Health'][tid] = Health(current_hp=100, max_hp=100)

    # Fast projectile moving right, small hit radius to stress tunneling
    pid = world.create_entity()
    world.components['Position'][pid] = Position(120, 100)
    world.components['Velocity'][pid] = Velocity(30, 0)
    fb = FireballComponent(dx=30, dy=0, damage=20, lifespan=120, caster=None, spell_key='t_rect', spawn_pos=(120, 100), hit_radius=2.0)
    world.components['FireballComponent'][pid] = fb

    # Run a few steps; should collide and be removed, HP reduced
    removed = _step_until_removed(world, sys_fb, pid, max_steps=20)
    assert removed, "Projectile should be removed after hitting rect collider"
    hp = world.components['Health'][tid]
    assert hp.current_hp == 80


def test_fireball_hits_mask_collider(world, sys_fb):
    _ensure_maps(world)
    # Target mask: 16x16 filled square
    tid = world.create_entity()
    world.components['Position'][tid] = Position(200, 100)
    surf = pygame.Surface((16, 16), pygame.SRCALPHA)
    surf.fill((255, 255, 255, 255))
    mask = pygame.mask.from_surface(surf)
    mcol = MaskCollider(mask=mask, offset_x=-8, offset_y=-8)
    world.components['MultiCollider'][tid] = MultiCollider({'mask': mcol})
    world.components['Health'][tid] = Health(current_hp=50, max_hp=50)

    # Fast projectile moving right
    pid = world.create_entity()
    world.components['Position'][pid] = Position(120, 100)
    world.components['Velocity'][pid] = Velocity(28, 0)
    fb = FireballComponent(dx=28, dy=0, damage=10, lifespan=120, caster=None, spell_key='t_mask', spawn_pos=(120, 100), hit_radius=2.0)
    world.components['FireballComponent'][pid] = fb

    removed = _step_until_removed(world, sys_fb, pid, max_steps=20)
    assert removed
    hp = world.components['Health'][tid]
    assert hp.current_hp == 40


def test_fireball_does_not_damage_neutral(world, sys_fb):
    _ensure_maps(world)
    # Neutral target
    tid = world.create_entity()
    world.components['Position'][tid] = Position(200, 100)
    body = Collider(width=24, height=24, offset_x=-12, offset_y=-12)
    world.components['MultiCollider'][tid] = MultiCollider({'body': body})
    world.components['Health'][tid] = Health(current_hp=30, max_hp=30)
    world.components['Identity'][tid] = Identity(id=tid, name='npc', title='', faction=Faction.NEUTRAL)

    # Projectile passing through
    pid = world.create_entity()
    world.components['Position'][pid] = Position(120, 100)
    world.components['Velocity'][pid] = Velocity(32, 0)
    fb = FireballComponent(dx=32, dy=0, damage=25, lifespan=120, caster=None, spell_key='t_neutral', spawn_pos=(120, 100), hit_radius=2.0)
    world.components['FireballComponent'][pid] = fb

    removed = _step_until_removed(world, sys_fb, pid, max_steps=20)
    assert removed, "Projectile should be removed on neutral hit"
    hp = world.components['Health'][tid]
    assert hp.current_hp == 30, "Neutral should not be damaged"


def test_fireball_does_not_damage_caster(world, sys_fb):
    _ensure_maps(world)
    # Caster with collider
    caster = world.create_entity()
    world.components['Position'][caster] = Position(150, 100)
    world.components['MultiCollider'][caster] = MultiCollider({'body': Collider(20, 20, -10, -10)})
    world.components['Health'][caster] = Health(current_hp=40, max_hp=40)

    # Projectile spawned from caster center moving away
    pid = world.create_entity()
    world.components['Position'][pid] = Position(150, 100)
    world.components['Velocity'][pid] = Velocity(25, 0)
    fb = FireballComponent(dx=25, dy=0, damage=10, lifespan=120, caster=caster, spell_key='t_self', spawn_pos=(150, 100), hit_radius=3.0)
    world.components['FireballComponent'][pid] = fb

    # Place another entity to ensure it can travel freely without self-hit
    tid = world.create_entity()
    world.components['Position'][tid] = Position(220, 100)
    world.components['MultiCollider'][tid] = MultiCollider({'body': Collider(20, 20, -10, -10)})
    world.components['Health'][tid] = Health(current_hp=30, max_hp=30)

    removed = _step_until_removed(world, sys_fb, pid, max_steps=20)
    assert removed
    # Caster HP unchanged
    assert world.components['Health'][caster].current_hp == 40
    # Target was hit
    assert world.components['Health'][tid].current_hp == 20


def test_fireball_removed_by_range(world, sys_fb, monkeypatch):
    _ensure_maps(world)
    # Monkeypatch the SPELLS dict referenced inside the fireball_system module
    monkeypatch.setattr(fireball_system_mod, 'SPELLS', {'t_range': {'range': 50}}, raising=False)

    pid = world.create_entity()
    world.components['Position'][pid] = Position(0, 0)
    world.components['Velocity'][pid] = Velocity(40, 0)
    fb = FireballComponent(dx=40, dy=0, damage=1, lifespan=120, caster=None, spell_key='t_range', spawn_pos=(0, 0), hit_radius=2.0)
    world.components['FireballComponent'][pid] = fb

    removed = _step_until_removed(world, sys_fb, pid, max_steps=10)
    assert removed, "Projectile should be removed after exceeding range"
    assert pid not in world.components.get('FireballComponent', {})


def test_hit_radius_multiplier_effect(world, sys_fb):
    _ensure_maps(world)
    # Target with a circle-like collider emulated by a small rect; place slightly offset
    tid = world.create_entity()
    world.components['Position'][tid] = Position(200, 100)
    world.components['MultiCollider'][tid] = MultiCollider({'body': Collider(6, 6, -3, -3)})
    world.components['Health'][tid] = Health(current_hp=10, max_hp=10)

    # Projectile traveling near the target; with small base radius it would miss without multiplier
    pid = world.create_entity()
    world.components['Position'][pid] = Position(200, 90)
    world.components['Velocity'][pid] = Velocity(0, 20)
    fb = FireballComponent(dx=0, dy=20, damage=5, lifespan=120, caster=None, spell_key='t_mul', spawn_pos=(200, 90), hit_radius=8.0)
    world.components['FireballComponent'][pid] = fb

    removed = _step_until_removed(world, sys_fb, pid, max_steps=10)
    assert removed
    assert world.components['Health'][tid].current_hp == 5


def test_fireball_hits_circle_collider(world, sys_fb):
    _ensure_maps(world)
    # Target with a circular collider implemented via a simple object
    class CircleCol:
        def __init__(self, r, ox=0, oy=0):
            self.radius = r
            self.offset_x = ox
            self.offset_y = oy

    tid = world.create_entity()
    world.components['Position'][tid] = Position(200, 100)
    world.components['MultiCollider'][tid] = MultiCollider({'feet': CircleCol(10, 0, 0)})
    world.components['Health'][tid] = Health(current_hp=12, max_hp=12)

    pid = world.create_entity()
    world.components['Position'][pid] = Position(160, 100)
    world.components['Velocity'][pid] = Velocity(16, 0)
    world.components['FireballComponent'][pid] = FireballComponent(dx=16, dy=0, damage=4, lifespan=60, caster=None, spell_key='t_circle', spawn_pos=(160, 100), hit_radius=4.0)

    removed = _step_until_removed(world, sys_fb, pid, max_steps=10)
    assert removed
    assert world.components['Health'][tid].current_hp == 8
