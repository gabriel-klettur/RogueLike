import pygame

from roguelike_game.ecs.components.particles.particle_component import ParticleComponent
from roguelike_game.ecs.components.particles.dash_emitter_component import DashEmitterComponent
from roguelike_game.ecs.components.particles.slash_emitter_component import SlashEmitterComponent


def test_particle_component_construction_and_defaults():
    p = ParticleComponent(dx=1.0, dy=-0.5, color=(10, 20, 30), size=4, lifespan=60, anchor_eid=123)
    assert p.dx == 1.0
    assert p.dy == -0.5
    assert p.color == (10, 20, 30)
    assert p.size == 4
    assert p.lifespan == 60
    assert p.age == 0
    assert p.anchor_eid == 123
    assert p.anchor_last_x is None and p.anchor_last_y is None


def test_dash_emitter_component_dataclass_construction():
    comp = DashEmitterComponent(count=6, lifespan=30, size_range=(2, 6),
                                color_choices=((255, 255, 255), (100, 100, 255)),
                                speed_range=(10.0, 30.0))
    assert comp.count == 6
    assert comp.lifespan == 30
    assert comp.size_range == (2, 6)
    assert comp.color_choices[0] == (255, 255, 255)
    assert comp.speed_range == (10.0, 30.0)


def test_slash_emitter_component_dataclass_construction():
    comp = SlashEmitterComponent(radius=24.0, arc_range=1.57, count=12, lifespan=20,
                                 size_range=(2, 5), color=(255, 0, 0), speed_multiplier=1.2,
                                 direction=(1.0, 0.0), offset=8.0)
    assert comp.radius == 24.0
    assert comp.arc_range == 1.57
    assert comp.count == 12
    assert comp.lifespan == 20
    assert comp.size_range == (2, 5)
    assert comp.color == (255, 0, 0)
    assert comp.speed_multiplier == 1.2
    assert comp.direction == (1.0, 0.0)
    assert comp.offset == 8.0
