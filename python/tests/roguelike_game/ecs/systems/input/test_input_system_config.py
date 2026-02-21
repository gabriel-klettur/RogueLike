import types

from roguelike_game.ecs.systems.input import input_system as mod


def test_update_calls_config_load(monkeypatch):
    calls = {'load': 0}

    class FakeInputConfig:
        def __init__(self, config_path=None):
            pass
        def _load(self):
            calls['load'] += 1
        def get_keys_for_action(self, action: str):
            return []

    class FakeKeys:
        def __getitem__(self, _):
            return 0

    monkeypatch.setattr(mod, 'InputConfig', FakeInputConfig, raising=True)
    monkeypatch.setattr(mod.pygame.key, 'get_pressed', lambda: FakeKeys(), raising=True)
    monkeypatch.setattr(mod.pygame, 'K_F4', 9999, raising=False)
    monkeypatch.setattr(mod.pygame.key, 'get_mods', lambda: 0, raising=True)

    sys = mod.InputSystem(perf_log=None, config_path=None)

    class DummyWorld:
        components = {}
        state = types.SimpleNamespace(buildings_editor_active=False, particles_editor_visible=False)

    sys.update(DummyWorld())

    assert calls['load'] == 1
