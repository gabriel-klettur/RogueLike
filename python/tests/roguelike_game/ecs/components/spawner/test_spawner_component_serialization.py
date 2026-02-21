import dataclasses

from roguelike_game.ecs.components.spawner.spawner_config import SpawnerConfig
from roguelike_game.ecs.components.spawner.spawner_state import SpawnerState


def test_spawner_config_asdict_roundtrip_keys():
    cfg = SpawnerConfig(
        template_id="t",
        zone="z",
        anchor_tile=(0, 0),
        spawner_type="invisible",
        trigger={"type": "proximity", "radius": 1, "auto_start": True},
        policy={"mode": "periodic", "cooldown_s": 1.0, "max_active": 1, "persistent": False},
        waves=[{"spawns": []}],
        cooldown_frames=60,
        restart_cooldown_frames=120,
        between_waves_cooldown_frames=0,
        spawn_radius=None,
        spawner_shape="circle",
        defend_spawn=False,
        defend_leash=True,
        visible_in_game=False,
        building_id=None,
        state_visuals=None,
        visuals_offsets_px=None,
        visuals_split_ratio=None,
        life_defaults=None,
        hp_scope="per_state",
        visuals_life=None,
    )
    data = dataclasses.asdict(cfg)
    assert data["template_id"] == "t"
    assert data["zone"] == "z"
    assert data["anchor_tile"] == (0, 0)
    assert data["spawner_shape"] == "circle"


def test_spawner_state_asdict_defaults():
    st = SpawnerState()
    data = dataclasses.asdict(st)
    assert data["fsm_state"] == "await_trigger"
