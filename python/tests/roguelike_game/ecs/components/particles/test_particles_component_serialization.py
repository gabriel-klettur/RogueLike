import dataclasses

from roguelike_game.ecs.components.particles.dash_emitter_component import DashEmitterComponent
from roguelike_game.ecs.components.particles.slash_emitter_component import SlashEmitterComponent


def test_dash_emitter_asdict():
    comp = DashEmitterComponent(count=3, lifespan=10, size_range=(1, 2),
                                color_choices=((1, 2, 3),), speed_range=(0.1, 0.2))
    data = dataclasses.asdict(comp)
    assert data["count"] == 3
    assert data["lifespan"] == 10
    assert data["size_range"] == (1, 2)
    # Accept either tuple-of-tuples or list-of-tuples from asdict
    assert list(data["color_choices"]) == [(1, 2, 3)]
    assert data["speed_range"] == (0.1, 0.2)


def test_slash_emitter_asdict():
    comp = SlashEmitterComponent(radius=10.0, arc_range=1.0, count=5, lifespan=12,
                                 size_range=(2, 3), color=(9, 9, 9), speed_multiplier=1.0,
                                 direction=(0.0, 1.0), offset=4.0)
    data = dataclasses.asdict(comp)
    assert data["radius"] == 10.0
    assert data["arc_range"] == 1.0
    assert data["count"] == 5
    assert data["lifespan"] == 12
    assert data["size_range"] == (2, 3)
    assert data["color"] == (9, 9, 9)
    assert data["speed_multiplier"] == 1.0
    assert data["direction"] == (0.0, 1.0)
    assert data["offset"] == 4.0
