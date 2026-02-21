from roguelike_game.ecs.components.spawner.spawner_config import SpawnerConfig
from roguelike_game.ecs.components.spawner.spawner_state import SpawnerState
from roguelike_game.ecs.components.spawner.spawner_child import SpawnerChild


def test_spawner_config_minimal_fields_and_defaults():
    cfg = SpawnerConfig(
        template_id="tmpl_basic",
        zone="zone_01",
        anchor_tile=(5, 7),
        spawner_type="invisible",
        trigger={"type": "proximity", "radius": 3, "auto_start": True},
        policy={"mode": "periodic", "cooldown_s": 1.0, "max_active": 5, "persistent": False},
        waves=[{"spawns": [{"kind": "monster", "id": "goblin", "count": 2, "spread_radius": 1}]}],
    )
    assert cfg.template_id == "tmpl_basic"
    assert cfg.zone == "zone_01"
    assert cfg.anchor_tile == (5, 7)
    assert cfg.spawner_type == "invisible"
    assert isinstance(cfg.trigger, dict)
    assert isinstance(cfg.policy, dict)
    assert isinstance(cfg.waves, list)
    # Defaults present
    assert cfg.cooldown_frames == 0
    assert cfg.restart_cooldown_frames == 0
    assert cfg.between_waves_cooldown_frames == 0
    assert cfg.spawn_radius is None
    assert cfg.spawner_shape == "circle"
    assert cfg.defend_spawn is False
    assert cfg.defend_leash is True
    assert cfg.visible_in_game is False
    assert cfg.building_id is None
    assert cfg.state_visuals is None
    assert cfg.visuals_offsets_px is None
    assert cfg.visuals_split_ratio is None
    assert cfg.life_defaults is None
    assert cfg.hp_scope == "per_state"
    assert cfg.visuals_life is None


def test_spawner_state_defaults():
    st = SpawnerState()
    assert st.started is False
    assert st.current_wave_idx == 0
    assert st.cooldown_remaining == 0
    assert st.spawned_entities == []
    assert st.spawned_this_wave is False
    assert st.current_wave_entities == set()
    assert st.expected_this_wave == 0
    assert st.finished is False
    assert st.restart_cooldown_remaining == 0
    assert st.active_entities == set()
    assert st.initial_proximity_done is False
    assert st.fsm_state == "await_trigger"
    assert st.fsm_set_id is None
    assert st.fsm_set_params == {}
    assert st.visual_override_token is None


def test_spawner_child_construction():
    ch = SpawnerChild(spawner_eid=123, wave_idx=2)
    assert ch.spawner_eid == 123
    assert ch.wave_idx == 2
