from roguelike_game.ecs.components.spawn.spawn_request import SpawnRequest
from roguelike_game.ecs.components.spawn.spawn_stabilizer import SpawnStabilizer


def test_spawn_request_defaults_and_fields():
    req = SpawnRequest(prototype="goblin", position=(10, 20))
    assert req.prototype == "goblin"
    assert req.position == (10, 20)
    assert req.instance_id is None
    assert req.spawner_eid is None
    assert req.wave_idx is None
    assert req.defend_center is None
    assert req.defend_radius_px is None
    assert req.defend_leash is None
    assert req.defend_shape is None


def test_spawn_stabilizer_defaults():
    stab = SpawnStabilizer()
    assert stab.frames_remaining == 7
    assert stab.max_search_radius == 12
