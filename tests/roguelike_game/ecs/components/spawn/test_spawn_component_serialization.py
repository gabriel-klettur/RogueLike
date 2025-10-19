import dataclasses

from roguelike_game.ecs.components.spawn.spawn_request import SpawnRequest
from roguelike_game.ecs.components.spawn.spawn_stabilizer import SpawnStabilizer


def test_spawn_request_asdict():
    req = SpawnRequest(prototype="orc", position=(1, 2), instance_id="i1", spawner_eid=42,
                       wave_idx=0, defend_center=(1.0, 2.0), defend_radius_px=32.0,
                       defend_leash=True, defend_shape="circle")
    data = dataclasses.asdict(req)
    assert data["prototype"] == "orc"
    assert data["position"] == (1, 2)
    assert data["spawner_eid"] == 42
    assert data["defend_shape"] == "circle"


def test_spawn_stabilizer_asdict():
    stab = SpawnStabilizer(frames_remaining=5, max_search_radius=10)
    assert dataclasses.asdict(stab) == {"frames_remaining": 5, "max_search_radius": 10}
