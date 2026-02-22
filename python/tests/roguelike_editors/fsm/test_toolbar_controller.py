from __future__ import annotations

from types import SimpleNamespace

from roguelike_editors.fsm.fsm_toolbar.fsm_toolbar_controller import FsmToolbarController
from roguelike_editors.fsm.fsm_toolbar.fsm_toolbar_model import FsmToolbarModel


class _StubToolbar:
    def __init__(self, consume=False, raise_exc=False):
        self.consume = consume
        self.raise_exc = raise_exc
        self.events = []

    def handle_event(self, event):
        self.events.append(event)
        if self.raise_exc:
            raise RuntimeError("boom")
        return self.consume


class _StubView:
    def __init__(self, toolbar):
        self.toolbar = toolbar
        self.ensure_ready_called = 0

    def ensure_ready(self, model):
        assert isinstance(model, FsmToolbarModel)
        self.ensure_ready_called += 1

    def render(self, model, screen):
        return None


def test_handle_event_calls_ensure_ready_and_delegates_to_toolbar(monkeypatch):
    model = FsmToolbarModel()
    toolbar = _StubToolbar(consume=True)
    view = _StubView(toolbar)
    ctl = FsmToolbarController(model=model, view=view)

    # Ensure events handler returns False so only toolbar decides
    ctl.events.handle_event = lambda c, e: False

    ev = SimpleNamespace(kind='dummy')
    consumed = ctl.handle_event(ev)

    assert consumed is True
    assert view.ensure_ready_called == 1
    assert toolbar.events[-1] is ev


def test_handle_event_falls_back_to_events_when_toolbar_errors(monkeypatch):
    model = FsmToolbarModel()
    toolbar = _StubToolbar(raise_exc=True)
    view = _StubView(toolbar)
    ctl = FsmToolbarController(model=model, view=view)

    # events handler should be used and return True
    ctl.events.handle_event = lambda c, e: True

    ev = SimpleNamespace(kind='dummy')
    consumed = ctl.handle_event(ev)

    assert consumed is True
    assert view.ensure_ready_called == 1


def test_active_tool_helpers():
    model = FsmToolbarModel()
    ctl = FsmToolbarController(model=model, view=_StubView(_StubToolbar()))

    assert ctl.is_active('sets_list') is False
    ctl.set_active('sets_list')
    assert ctl.is_active('sets_list') is True
    ctl.set_active(None)
    assert ctl.is_active('sets_list') is False
