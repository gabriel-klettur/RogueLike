from __future__ import annotations

import sys
from pathlib import Path
import pytest

# Ensure 'src' is importable
ROOT = Path(__file__).resolve().parents[1]
src_path = ROOT / 'src'
if str(src_path) not in sys.path:
    sys.path.insert(0, str(src_path))

from roguelike_game.config.spells_config import SPELLS  # type: ignore
from roguelike_game.ecs.components.transform.position import Position  # type: ignore
from roguelike_game.ecs.components.abilities.meteor_fall_component import MeteorFallComponent  # type: ignore
from roguelike_game.ecs.components.stats.health import Health  # type: ignore
from roguelike_game.ecs.systems.combat.spells.meteor_fall_system import MeteorFallSystem  # type: ignore
from roguelike_game.ecs.utils.spell_vfx import get_impact_scale  # type: ignore


@pytest.fixture()
def cfg():
    return SPELLS.get('meteor_shower')


def _force_impact(world, camera, meteor_eid, monkeypatch):
    sys_under_test = MeteorFallSystem()

    class _Dummy:
        def get_size(self):
            return (10, 10)

    # Update 1 (initialize and move)
    monkeypatch.setattr(
        'roguelike_game.ecs.systems.combat.spells.meteor_fall_system.load_image',
        lambda path: _Dummy(),
        raising=True,
    )
    sys_under_test.update(world, camera)

    # Force y to target
    mfall = world.components['MeteorFallComponent'][meteor_eid]
    pos = world.components['Position'][meteor_eid]
    pos.y = float(mfall.target_y)

    # Update 2 (impact)
    sys_under_test.update(world, camera)


def test_impact_creates_single_puddle_with_radius_and_scale(world, camera, cfg, monkeypatch):
    owner = world.create_entity()
    x, y = 200.0, 300.0
    meteor = world.create_entity()
    world.components.setdefault('Position', {})[meteor] = Position(x, y - 10.0)
    world.components.setdefault('MeteorFallComponent', {})[meteor] = MeteorFallComponent(
        target_x=x, target_y=y, height_px=10.0, fall_speed_px_s=1000.0,
        impact_damage=40.0, impact_radius=160.0, owner=owner, spell_key='meteor_shower')

    _force_impact(world, camera, meteor, monkeypatch)

    puddles = world.components.get('PuddleComponent', {})
    positions = world.components.get('Position', {})
    marks = [eid for eid, _ in puddles.items() if abs(positions.get(eid).x - x) <= 0.5 and abs(positions.get(eid).y - y) <= 0.5]
    assert len(marks) == 1
    mark = marks[0]
    assert abs(float(puddles[mark].radius) - 160.0) < 1e-4

    scale_map = world.components.get('Scale', {})
    expected_scale = float(get_impact_scale(cfg, 0.10))
    assert mark in scale_map
    assert abs(float(getattr(scale_map[mark], 'scale', 0.0)) - expected_scale) < 1e-4


def test_damage_40_excludes_owner(world, camera, monkeypatch):
    owner = world.create_entity()
    victim = world.create_entity()
    outsider = world.create_entity()
    world.components.setdefault('Health', {})[victim] = Health(max_hp=100, current_hp=100)
    world.components.setdefault('Health', {})[outsider] = Health(max_hp=100, current_hp=100)
    world.components.setdefault('Health', {})[owner] = Health(max_hp=100, current_hp=100)

    x, y = 400.0, 400.0
    world.components.setdefault('Position', {})[victim] = Position(x + 50.0, y)
    world.components.setdefault('Position', {})[outsider] = Position(x + 300.0, y)
    world.components.setdefault('Position', {})[owner] = Position(x, y)

    meteor = world.create_entity()
    world.components.setdefault('Position', {})[meteor] = Position(x, y - 10.0)
    world.components.setdefault('MeteorFallComponent', {})[meteor] = MeteorFallComponent(
        target_x=x, target_y=y, height_px=10.0, fall_speed_px_s=1000.0,
        impact_damage=40.0, impact_radius=160.0, owner=owner, spell_key='meteor_shower')

    _force_impact(world, camera, meteor, monkeypatch)

    hmap = world.components.get('Health', {})
    assert hmap[owner].current_hp == 100
    assert hmap[outsider].current_hp == 100
    assert hmap[victim].current_hp == 60


def test_prevent_duplicate_mark_same_position(world, camera, monkeypatch):
    owner = world.create_entity()
    x, y = 500.0, 600.0

    m1 = world.create_entity()
    world.components.setdefault('Position', {})[m1] = Position(x, y - 10.0)
    world.components.setdefault('MeteorFallComponent', {})[m1] = MeteorFallComponent(
        target_x=x, target_y=y, height_px=10.0, fall_speed_px_s=1000.0,
        impact_damage=40.0, impact_radius=160.0, owner=owner, spell_key='meteor_shower')
    _force_impact(world, camera, m1, monkeypatch)

    m2 = world.create_entity()
    world.components.setdefault('Position', {})[m2] = Position(x, y - 10.0)
    world.components.setdefault('MeteorFallComponent', {})[m2] = MeteorFallComponent(
        target_x=x, target_y=y, height_px=10.0, fall_speed_px_s=1000.0,
        impact_damage=40.0, impact_radius=160.0, owner=owner, spell_key='meteor_shower')
    _force_impact(world, camera, m2, monkeypatch)

    puddles = world.components.get('PuddleComponent', {})
    positions = world.components.get('Position', {})
    marks = [eid for eid, _ in puddles.items() if abs(positions.get(eid).x - x) <= 0.5 and abs(positions.get(eid).y - y) <= 0.5]
    assert len(marks) == 1
