import types

import roguelike_game.ecs.systems.core.spawn_system as ss


def test_spawn_system_creates_entity_and_cleans_request(monkeypatch):
    # Fake factory and route builder
    class FakeFactory:
        def __init__(self):
            self.calls = []
        def create(self, world, tile_x, tile_y, monster_type, instance_id=None):
            self.calls.append((tile_x, tile_y, monster_type, instance_id))
            return 42

    fake_factory = FakeFactory()
    monkeypatch.setattr(ss, 'get_factory', lambda name: fake_factory, raising=True)
    # Avoid heavy route building even if defend area used elsewhere
    monkeypatch.setattr(ss, 'build_patrol_route', lambda *a, **k: {'points': [], 'dwell_times': []}, raising=False)

    # World with a single SpawnRequest
    removed = []
    def remove_entity(eid):
        removed.append(eid)

    req_eid = 7
    req = types.SimpleNamespace(prototype='goblin', position=(10, 20))

    world = types.SimpleNamespace(
        components={
            'SpawnRequest': {req_eid: req},
            'SpawnStabilizer': {},
            'NPCState': {},
            'PatrolRoute': {},
            'SpawnerState': {},
        },
        remove_entity=remove_entity,
    )

    sys = ss.SpawnSystem(perf_log=None)
    sys.update(world)

    # Factory called and new entity returned
    assert fake_factory.calls and fake_factory.calls[0][2] == 'goblin'
    # SpawnStabilizer set for new entity
    assert 42 in world.components['SpawnStabilizer']
    # Request entity removed
    assert removed == [req_eid]
