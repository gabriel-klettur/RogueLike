from __future__ import annotations

import importlib
from types import SimpleNamespace

import roguelike_editors.fsm.fsm_editor_events as mod


class _StubController:
    def __init__(self):
        self.visible = False
        self.handled = []
        self.render_calls = 0

    def handle_event(self, event):
        self.handled.append(event)
        return getattr(event, 'consume', False)

    def render(self, screen):
        self.render_calls += 1


def test_event_handler_mirrors_config_and_delegates(monkeypatch):
    ctrl = _StubController()
    # Force get_controller to return our stub
    monkeypatch.setattr(mod, 'get_controller', lambda: ctrl, raising=True)

    # Case 1: not visible -> returns False, no delegation
    cfg = importlib.import_module('roguelike_engine.config.config')
    cfg.DEBUG_ENTITIES = False
    ev = SimpleNamespace(type='ANY', consume=True)
    assert mod.FsmEditorEventHandler.handle_event(ev) is False
    assert ctrl.handled == []

    # Case 2: visible -> delegates and returns underlying result
    cfg.DEBUG_ENTITIES = True
    ev2 = SimpleNamespace(type='ANY', consume=True)
    assert mod.FsmEditorEventHandler.handle_event(ev2) is True
    assert ctrl.handled[-1] is ev2


def test_render_mirrors_config_and_calls_controller(monkeypatch):
    ctrl = _StubController()
    monkeypatch.setattr(mod, 'get_controller', lambda: ctrl, raising=True)

    cfg = importlib.import_module('roguelike_engine.config.config')
    cfg.DEBUG_ENTITIES = False

    screen = SimpleNamespace()
    # Not visible -> no render
    mod.FsmEditorEventHandler.render(screen)
    assert ctrl.render_calls == 0

    # Visible -> render once
    cfg.DEBUG_ENTITIES = True
    mod.FsmEditorEventHandler.render(screen)
    assert ctrl.render_calls == 1
