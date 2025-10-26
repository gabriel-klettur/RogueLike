import types
import json
import pygame
import pytest

from roguelike_game.config.spells_config import load_spells_config
from roguelike_game.ecs.systems.combat.spells.resolvers_pkg.puddle import PuddleResolver
from roguelike_game.ecs.components.transform.position import Position
from roguelike_game.ecs.components.rendering.sprite import Sprite


class _World:
    def __init__(self):
        self.components = {
            'Position': {},
            'PuddleComponent': {},
            'Sprite': {},
            'Scale': {},
        }
        self._next = 1

    def create_entity(self):
        eid = self._next
        self._next += 1
        return eid


def test_puddle_resolver_spawns_with_expected_params(monkeypatch, tmp_path):
    # Minimal spells.json with puddle entry
    data = {
        'puddle_lava': {
            'id': 'puddle_lava',
            'type': 'puddle',
            'name': 'Lava Puddle',
            'mana_cost': 1,
            'timings': {'prepare': 0.1, 'cooldown': 1.0},
            'rules': {'allow_movement': True, 'lock_cast_direction': True},
            'constraints': {'allow_overlap': True},
            'effect': {
                'radius': 16,
                'duration': 5.0,
                'tick_period': 0.25,
                'damage': 0,
                'element': 'lava',
                'status': {'burn': {'dps': 5, 'duration': 3.0, 'tick_period': 1.0}}
            },
            'vfx': {
                'particles': {'color': [255, 120, 60]},
                'alpha': 96
            }
        }
    }
    p = tmp_path / 'spells.json'
    p.write_text(json.dumps(data), encoding='utf-8')

    spells = load_spells_config(p)
    cfg = spells['puddle_lava']

    world = _World()
    caster = world.create_entity()
    world.components['Position'][caster] = Position(10, 20)

    # Force spawn at a given position
    spawn_meta = {'spawn_pos': (100, 200)}

    resolver = PuddleResolver()
    resolver.resolve(world, caster, spawn_meta=spawn_meta, cfg=cfg, camera=None)

    # There should be exactly one puddle component created
    puddles = world.components['PuddleComponent']
    assert len(puddles) == 1
    peid, pc = next(iter(puddles.items()))

    # Position at spawn_meta and parameters carried over
    pos = world.components['Position'][peid]
    assert (pos.x, pos.y) == (100.0, 200.0)
    assert int(pc.radius) == 16
    assert pytest.approx(pc.duration, abs=1e-6) == 5.0
    assert pytest.approx(pc.tick_period, abs=1e-6) == 0.25
    assert pc.element == 'lava'
    # Color and alpha propagated to render
    assert pc.color == (255, 120, 60)
    assert pc.alpha == 96


def test_puddle_resolver_defaults_to_caster_center(monkeypatch):
    # No schema validation needed here; we're not loading from file
    cfg = {
        'id': 'puddle_lava', 'type': 'puddle',
        'effect': {'radius': 10, 'duration': 1.0, 'tick_period': 0.5, 'element': 'lava'},
        'vfx': {'particles': {'color': [255, 120, 60]}, 'alpha': 96},
    }
    world = _World()
    caster = world.create_entity()
    # Caster at (10,20) with a 16x16 sprite -> center at (18,28)
    world.components['Position'][caster] = Position(10, 20)
    world.components['Sprite'][caster] = Sprite(pygame.Surface((16, 16), pygame.SRCALPHA))

    resolver = PuddleResolver()
    resolver.resolve(world, caster, spawn_meta=None, cfg=cfg, camera=None)

    puddles = world.components['PuddleComponent']
    assert len(puddles) == 1
    peid = next(iter(puddles.keys()))
    pos = world.components['Position'][peid]
    assert (pos.x, pos.y) == (18.0, 28.0)
