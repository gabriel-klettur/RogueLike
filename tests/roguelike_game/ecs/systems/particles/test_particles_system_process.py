import types

from roguelike_game.ecs.systems.particles.particle_system import ParticleSystem


def test_particle_moves_ages_and_expires():
    removed = []
    def remove_entity(eid):
        removed.append(eid)

    # Partícula con lifespan 2: debe eliminarse tras dos updates
    eid = 1
    pos = types.SimpleNamespace(x=0.0, y=0.0)
    comp = types.SimpleNamespace(dx=1.5, dy=-0.5, age=0, lifespan=2, anchor_eid=None)

    world = types.SimpleNamespace(
        components={
            'Position': {eid: pos},
            'ParticleComponent': {eid: comp},
        },
        remove_entity=remove_entity,
    )

    sys = ParticleSystem(perf_log=None)
    # 1st update: moves and ages -> age=1, not removed
    sys.update(world)
    assert pos.x == 1.5 and pos.y == -0.5
    assert comp.age == 1
    assert removed == []
    # 2nd update: moves and ages -> age=2, removed
    sys.update(world)
    assert removed == [eid]


def test_particle_follows_anchor_delta():
    removed = []
    def remove_entity(eid):
        removed.append(eid)

    eid = 2
    anchor_id = 9
    # Anchor moved by (3, -2) between updates; particle should inherit this delta
    pos = types.SimpleNamespace(x=10.0, y=10.0)
    anchor_pos = types.SimpleNamespace(x=5.0, y=5.0)
    comp = types.SimpleNamespace(dx=0.0, dy=0.0, age=0, lifespan=5, anchor_eid=anchor_id, anchor_last_x=None, anchor_last_y=None)

    world = types.SimpleNamespace(
        components={
            'Position': {eid: pos, anchor_id: anchor_pos},
            'ParticleComponent': {eid: comp},
        },
        remove_entity=remove_entity,
    )

    sys = ParticleSystem(perf_log=None)
    # First update: initializes anchor_last and does not move by anchor delta
    sys.update(world)
    assert pos.x == 10.0 and pos.y == 10.0
    # Move anchor
    anchor_pos.x += 3.0
    anchor_pos.y += -2.0
    # Second update: apply anchor delta to particle pos
    sys.update(world)
    assert pos.x == 13.0 and pos.y == 8.0
