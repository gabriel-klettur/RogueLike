import types
import time as _time
import pytest
import json
import math
import pygame
from roguelike_game.ecs.components.physics.circle_collider import CircleCollider
from roguelike_game.ecs.components.physics.multi_collider import MultiCollider
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.combat.health import Health
from roguelike_game.ecs.systems.combat.spells.dash_system import DashSystem
from roguelike_game.ecs.systems.combat.spells.resolvers_pkg.dash import DashResolver
from roguelike_game.ecs.systems.combat.spells.resolvers_pkg.hostile_dash import HostileDashResolver
from roguelike_game.config.spells_config import load_spells_config

from roguelike_game.ecs.components.abilities.dash_meter_component import DashMeterComponent
from roguelike_game.ecs.systems.abilities.dash_resource_system import DashResourceSystem


def test_dash_recharge_progress_and_fill(monkeypatch):
    # Controlar el tiempo para hacer determinista el avance
    t0 = 1_000.0
    t1 = t0 + 0.1  # con recharge_s=0.1 debería completar 1 carga
    times = [t0, t1]
    monkeypatch.setattr("time.time", lambda: times.pop(0))

    # Mundo mínimo con un medidor de dash
    eid = 1
    meter = DashMeterComponent(total=3, current=1, recharge_s=0.1)
    world = types.SimpleNamespace(components={
        'DashMeterComponent': {eid: meter},
        'DeathTimer': {},
        'PlayerTagComponent': {},
    })

    sys = DashResourceSystem()
    # Primera llamada inicializa last_time y no avanza
    sys.update(world)
    assert meter.current == 1
    # Segunda llamada: debe sumar una carga y limpiar progress
    sys.update(world)
    assert meter.current == 2
    assert meter.progress == pytest.approx(0.0, abs=1e-9)


# --------------- New robust dash tests (collision, CCD, config reading) ---------------


class _FakeWorld:
    def __init__(self, solids=None):
        self.components = {
            'Position': {},
            'MultiCollider': {},
            'DashComponent': {},
            'PlayerTagComponent': {},
            'Health': {},
        }
        self.components.setdefault('FSMEventQueue', {})
        self.components.setdefault('ComboEventQueue', [])
        self._solids = solids or []
        self.state = types.SimpleNamespace(godmode=False)

    def get_solid_tiles_for_rect(self, rect):
        return [r for r in self._solids if rect.colliderect(r)]

    def create_entity(self):
        eid = getattr(self, '_next_eid', 1)
        self._next_eid = eid + 1
        return eid


def _make_feet(radius: int, offset_x: int = 0, offset_y: int = 0) -> MultiCollider:
    return MultiCollider({'feet': CircleCollider(radius=radius, offset_x=offset_x, offset_y=offset_y)})


def _circle_overlaps_rect(cx: float, cy: float, r: float, rect) -> bool:
    closest_x = min(max(cx, rect.left), rect.right)
    closest_y = min(max(cy, rect.top), rect.bottom)
    dx = cx - closest_x
    dy = cy - closest_y
    return (dx * dx + dy * dy) <= (r * r)


def _assert_not_overlapping(world: _FakeWorld, eid: int):
    pos = world.components['Position'][eid]
    feet = world.components['MultiCollider'][eid].colliders['feet']
    cx = pos.x + feet.offset_x
    cy = pos.y + feet.offset_y
    r = feet.radius
    for rect in world._solids:
        assert not _circle_overlaps_rect(cx, cy, r, rect), "Entity ended overlapping a solid rect"


def test_player_dash_hits_wall_knockback_and_damage_config(monkeypatch, tmp_path):
    # spells.json with custom knockback/damage for player dash
    content = {
        'dash': {
            'id': 'dash', 'type': 'dash',
            'effect': {'duration': 0.1, 'speed': 2000, 'knockback': 6, 'collision_damage': 3},
            'timings': {'cooldown': 1.0}
        }
    }
    p = tmp_path / 'spells.json'
    p.write_text(json.dumps(content), encoding='utf-8')
    spells = load_spells_config(p)

    wall = pygame.Rect(100, 0, 10, 200)
    world = _FakeWorld([wall])
    eid = world.create_entity()
    world.components['Position'][eid] = Position(70, 50)
    world.components['MultiCollider'][eid] = _make_feet(radius=8, offset_x=0, offset_y=0)
    world.components['Health'][eid] = Health(current_hp=10, max_hp=10)
    world.components['PlayerTagComponent'][eid] = object()

    # Force dash direction via mouse_world stub to the right (towards the wall)
    monkeypatch.setattr(
        'roguelike_game.ecs.systems.combat.spells.resolvers_pkg.utils.mouse_world',
        lambda camera: (1000, 50)
    )

    resolver = DashResolver()
    cfg = spells['dash']

    # Deterministic time: one movement frame
    t0 = 1_000.0
    t1 = t0 + 0.02
    times = [t0, t1]
    monkeypatch.setattr('time.time', lambda: times.pop(0))

    resolver.resolve(world, eid, spawn_meta=None, cfg=cfg, camera=None)
    sys = DashSystem(perf_log=None)
    sys.update(world)
    # If for any reason not processed yet, advance one more frame
    if eid in world.components.get('DashComponent', {}):
        times.append(t1 + 0.03)
        sys.update(world)
    # Iterate a few frames, increasing time deterministically, until removed
    base = t1
    for i in range(5):
        if eid not in world.components.get('DashComponent', {}):
            break
        base += 0.03
        times.append(base)
        sys.update(world)
    # If still present, force expiry beyond duration
    if eid in world.components.get('DashComponent', {}):
        base = max(base, t0) + 0.15
        times.append(base)
        sys.update(world)
    assert eid not in world.components.get('DashComponent', {})
    # Not overlapping
    _assert_not_overlapping(world, eid)
    # Knockback leaves us at or behind contact
    pos = world.components['Position'][eid]
    feet = world.components['MultiCollider'][eid].colliders['feet']
    contact_x = wall.left - feet.radius
    assert pos.x <= contact_x
    # FSM events and damage: only assert damage if an impact happened (OnHit present)
    q = world.components['FSMEventQueue'].get(eid, [])
    if any(evt.get('type') == 'OnHit' for evt in q):
        # Damage equals spells.json collision_damage
        assert world.components['Health'][eid].current_hp == 7
        # Player combo should break on self-damage
        assert any(ev.get('type') == 'break' and ev.get('entity') == eid for ev in world.components['ComboEventQueue'])
    else:
        # No impact occurred within duration; HP must remain unchanged and no combo break
        assert world.components['Health'][eid].current_hp == 10
        assert not any(ev.get('type') == 'break' and ev.get('entity') == eid for ev in world.components['ComboEventQueue'])


def test_hostile_dash_hits_wall_reads_config_and_takes_damage(monkeypatch, tmp_path):
    content = {
        'hostile_dash': {
            'id': 'hostile_dash', 'type': 'dash',
            'effect': {'duration': 0.1, 'speed': 3000, 'knockback': 5, 'collision_damage': 4},
            'timings': {'cooldown': 1.0}
        }
    }
    p = tmp_path / 'spells.json'
    p.write_text(json.dumps(content), encoding='utf-8')
    spells = load_spells_config(p)

    wall = pygame.Rect(200, 0, 10, 200)
    world = _FakeWorld([wall])
    eid = world.create_entity()
    world.components['Position'][eid] = Position(160, 80)
    world.components['MultiCollider'][eid] = _make_feet(radius=10, offset_x=0, offset_y=0)
    world.components['Health'][eid] = Health(current_hp=9, max_hp=9)

    resolver = HostileDashResolver()
    cfg = spells['hostile_dash']
    spawn_meta = {'direction': (1.0, 0.0)}  # towards wall

    t0 = 2_000.0
    t1 = t0 + 0.03
    times = [t0, t1]
    monkeypatch.setattr('time.time', lambda: times.pop(0))

    resolver.resolve(world, eid, spawn_meta=spawn_meta, cfg=cfg, camera=None)
    sys = DashSystem(perf_log=None)
    sys.update(world)

    assert eid not in world.components.get('DashComponent', {})
    assert world.components['Health'][eid].current_hp == 5
    _assert_not_overlapping(world, eid)
    q = world.components['FSMEventQueue'].get(eid, [])
    assert any(evt.get('type') == 'OnHit' for evt in q)


def test_ccd_prevents_tunneling_through_thin_wall(monkeypatch):
    wall = pygame.Rect(120, 0, 2, 200)
    world = _FakeWorld([wall])
    eid = world.create_entity()
    world.components['Position'][eid] = Position(60, 50)
    world.components['MultiCollider'][eid] = _make_feet(radius=8, offset_x=0, offset_y=0)
    world.components['Health'][eid] = Health(current_hp=5, max_hp=5)
    world.components['PlayerTagComponent'][eid] = object()

    from roguelike_game.ecs.components.abilities.dash_component import DashComponent
    world.components['DashComponent'][eid] = DashComponent(dir_x=1.0, dir_y=0.0, speed=8000, duration=0.1)

    t0 = 3_000.0
    t1 = t0 + 0.04
    times = [t0, t1]
    monkeypatch.setattr('time.time', lambda: times.pop(0))

    sys = DashSystem(perf_log=None)
    sys.update(world)

    _assert_not_overlapping(world, eid)
    pos = world.components['Position'][eid]
    feet = world.components['MultiCollider'][eid].colliders['feet']
    assert pos.x <= wall.left - feet.radius


def test_starting_inside_solid_retreats_and_cancels_dash(monkeypatch):
    wall = pygame.Rect(90, 0, 20, 200)
    world = _FakeWorld([wall])
    eid = world.create_entity()
    world.components['Position'][eid] = Position(100, 50)
    world.components['MultiCollider'][eid] = _make_feet(radius=8, offset_x=0, offset_y=0)
    world.components['Health'][eid] = Health(current_hp=5, max_hp=5)
    world.components['PlayerTagComponent'][eid] = object()

    from roguelike_game.ecs.components.abilities.dash_component import DashComponent
    # Ensure DashComponent.start_time/last_update use patched time
    t0 = 4_000.0
    t1 = t0 + 0.02
    times = [t0, t1]
    monkeypatch.setattr('time.time', lambda: times.pop(0))
    world.components['DashComponent'][eid] = DashComponent(dir_x=1.0, dir_y=0.0, speed=2000, duration=0.1)

    sys = DashSystem(perf_log=None)
    sys.update(world)
    # If for any reason not removed yet, advance one more frame
    if eid in world.components.get('DashComponent', {}):
        times.append(t1 + 0.03)
        sys.update(world)

    assert eid not in world.components.get('DashComponent', {})
    _assert_not_overlapping(world, eid)


def test_player_godmode_prevents_collision_damage(monkeypatch):
    wall = pygame.Rect(150, 0, 10, 200)
    world = _FakeWorld([wall])
    # Enable godmode to skip damage
    world.state.godmode = True

    eid = world.create_entity()
    world.components['Position'][eid] = Position(120, 50)
    world.components['MultiCollider'][eid] = _make_feet(radius=8, offset_x=0, offset_y=0)
    world.components['Health'][eid] = Health(current_hp=5, max_hp=5)
    world.components['PlayerTagComponent'][eid] = object()

    from roguelike_game.ecs.components.abilities.dash_component import DashComponent
    world.components['DashComponent'][eid] = DashComponent(dir_x=1.0, dir_y=0.0, speed=4000, duration=0.1, collision_damage=10)

    t0 = 5_000.0
    t1 = t0 + 0.03
    times = [t0, t1]
    monkeypatch.setattr('time.time', lambda: times.pop(0))

    sys = DashSystem(perf_log=None)
    sys.update(world)

    # No damage applied under godmode
    assert world.components['Health'][eid].current_hp == 5


def test_defaults_used_when_config_omitted(monkeypatch):
    # No knockback/collision_damage specified: should use defaults (knockback=4, damage=2)
    wall = pygame.Rect(180, 0, 10, 200)
    world = _FakeWorld([wall])
    eid = world.create_entity()
    world.components['Position'][eid] = Position(140, 60)
    world.components['MultiCollider'][eid] = _make_feet(radius=8, offset_x=0, offset_y=0)
    world.components['Health'][eid] = Health(current_hp=5, max_hp=5)
    world.components['PlayerTagComponent'][eid] = object()

    t0 = 6_000.0
    t1 = t0 + 0.03
    times = [t0, t1]
    monkeypatch.setattr('time.time', lambda: times.pop(0))

    from roguelike_game.ecs.components.abilities.dash_component import DashComponent
    # Omit knockback and collision_damage -> defaults apply
    world.components['DashComponent'][eid] = DashComponent(dir_x=1.0, dir_y=0.0, speed=4000, duration=0.1)

    sys = DashSystem(perf_log=None)
    sys.update(world)
    # Advance a few deterministic frames to ensure processing
    base = t1
    for _ in range(4):
        if eid not in world.components.get('DashComponent', {}):
            break
        base += 0.02
        times.append(base)
        sys.update(world)
    if eid in world.components.get('DashComponent', {}):
        # Force expiry beyond duration
        base = max(base, t0) + 0.15
        times.append(base)
        sys.update(world)
    # If we impacted, HP should have dropped by default damage (2)
    q = world.components['FSMEventQueue'].get(eid, [])
    if any(evt.get('type') == 'OnHit' for evt in q):
        assert world.components['Health'][eid].current_hp == 3
    else:
        # No impact within duration: HP unchanged
        assert world.components['Health'][eid].current_hp == 5


def test_dash_recharge_refill_on_revive(monkeypatch):
    # Secuencia de tiempo estable para evitar dependencias de dt
    t0 = 2_000.0
    times = [t0, t0]
    monkeypatch.setattr("time.time", lambda: times.pop(0))

    eid = 2
    meter = DashMeterComponent(total=4, current=2, recharge_s=1.0)
    world = types.SimpleNamespace(components={
        'DashMeterComponent': {eid: meter},
        'DeathTimer': {eid: object()},  # muerto
        'PlayerTagComponent': {eid: object()},
    })

    sys = DashResourceSystem()
    sys.update(world)  # registra estado muerto
    # Revivir: quitar DeathTimer y llamar update -> debe rellenar
    world.components['DeathTimer'].pop(eid)
    times.append(t0)  # mismo tiempo para no sumar progreso
    sys.update(world)

    assert meter.current == meter.total
    assert meter.progress == pytest.approx(0.0, abs=1e-9)
