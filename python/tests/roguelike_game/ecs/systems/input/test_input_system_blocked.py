import types

import pytest

from roguelike_game.ecs.systems.input import input_system as mod


def test_update_returns_early_when_blocked(monkeypatch):
    # Arrange: ensure pygame.key.get_pressed is NOT called
    class Boom:
        def __call__(self):
            raise AssertionError("get_pressed should not be called when input is blocked")

    # monkeypatch pygame key getter to explode if used
    monkeypatch.setattr(mod.pygame.key, 'get_pressed', Boom(), raising=True)

    called = {'blocked': False}

    def fake_block_reason(world):
        return "ui_modal_active"

    def fake_block_all_inputs_and_reset(self_obj, world):
        called['blocked'] = True

    monkeypatch.setattr(mod, 'block_reason', fake_block_reason, raising=True)
    monkeypatch.setattr(mod, 'block_all_inputs_and_reset', fake_block_all_inputs_and_reset, raising=True)

    sys = mod.InputSystem(perf_log=None, config_path=None)

    class DummyWorld:
        components = {}
        state = types.SimpleNamespace(buildings_editor_active=False, particles_editor_visible=False)

    # Act
    sys.update(DummyWorld())

    # Assert: block function executed, returned before keyboard read
    assert called['blocked'] is True
