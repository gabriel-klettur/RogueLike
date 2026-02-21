import types
from roguelike_game.ecs.systems.rendering.target_hud_render_system import TargetHudRenderSystem


def test_target_hud_shows_quemado_when_burning():
    world = types.SimpleNamespace(components={
        'TargetHUD': {'target_eid': 7, 'last_hit_time': 1_000.0, 'ttl_s': 3.0},
        'NPCState': {},
        'Identity': {},
        'Health': {7: types.SimpleNamespace(current_hp=5, max_hp=10)},
        'BurnComponent': {7: types.SimpleNamespace(start_time=1000.0, tick_period=1.0)},
    })

    sys = TargetHudRenderSystem(perf_log=None)
    # Call the internal label method to isolate logic
    label = sys._get_state_label(world, 7)
    assert label is not None
    assert 'Quemado' in label
