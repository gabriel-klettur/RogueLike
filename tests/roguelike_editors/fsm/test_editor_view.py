from __future__ import annotations

from types import SimpleNamespace

import roguelike_editors.fsm.fsm_editor_view as view_mod


class _StubTitleController:
    def __init__(self, *args, **kwargs):
        self.render_calls = 0

    def render(self, screen):
        self.render_calls += 1


def test_view_renders_title_when_visible(monkeypatch):
    # Monkeypatch TitleController and Model to lightweight versions
    monkeypatch.setattr(view_mod, 'FsmTitleController', _StubTitleController, raising=True)
    monkeypatch.setattr(view_mod, 'FsmTitleModel', lambda: SimpleNamespace(), raising=True)

    v = view_mod.FsmEditorView()
    # Controller stub with visible True
    ctrl = SimpleNamespace(visible=True)

    screen = SimpleNamespace(get_size=lambda: (1600, 800))

    # Render should call into stub title controller without exceptions
    v.render(ctrl, screen)
    # Access the internal stub to assert it was called
    assert isinstance(v._title_ctrl, _StubTitleController)
    assert v._title_ctrl.render_calls == 1


def test_view_noop_when_invisible(monkeypatch):
    monkeypatch.setattr(view_mod, 'FsmTitleController', _StubTitleController, raising=True)
    monkeypatch.setattr(view_mod, 'FsmTitleModel', lambda: SimpleNamespace(), raising=True)

    v = view_mod.FsmEditorView()
    ctrl = SimpleNamespace(visible=False)
    screen = SimpleNamespace(get_size=lambda: (1600, 800))

    v.render(ctrl, screen)
    # Title controller should not be created when not visible
    assert v._title_ctrl is None
