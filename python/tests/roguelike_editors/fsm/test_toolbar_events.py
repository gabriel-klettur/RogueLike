from __future__ import annotations

import sys
from types import SimpleNamespace

import pytest

import roguelike_editors.fsm.fsm_toolbar.fsm_toolbar_events as mod


class _FakeRect:
    def __init__(self, pos, size):
        self.x, self.y = pos
        self.w, self.h = size

    @property
    def right(self):
        return self.x + self.w

    @property
    def top(self):
        return self.y

    def collidepoint(self, pos):
        px, py = pos
        return self.x <= px <= self.x + self.w and self.y <= py <= self.y + self.h


class _FakeMouse:
    def __init__(self):
        self._pos = (0, 0)

    def get_pos(self):
        return self._pos

    def set_pos(self, p):
        self._pos = p


class _FakeSurface:
    def __init__(self, size):
        self._size = size

    def get_size(self):
        return self._size


class _FakePygame:
    KEYDOWN = object()
    MOUSEWHEEL = object()
    MOUSEBUTTONDOWN = object()
    MOUSEBUTTONUP = object()
    K_ESCAPE = 'ESC'
    K_s = 'S'

    def __init__(self):
        self.mouse = _FakeMouse()

    def Rect(self, pos, size):
        return _FakeRect(pos, size)


class _Ctrl:
    def __init__(self):
        self.model = SimpleNamespace(active_tool=None)
        panel = SimpleNamespace(pos=(0, 0), surface=_FakeSurface((200, 400)))
        self.view = SimpleNamespace(
            toolbar=SimpleNamespace(
                panel=panel,
                x=0,
                y=0,
                icon_rects={
                    'sets_list': _FakeRect((10, 10), (24, 24)),
                },
            )
        )

    def is_active(self, tool):
        return self.model.active_tool == tool

    def set_active(self, tool):
        self.model.active_tool = tool


@pytest.fixture(autouse=True)
def fake_pygame(monkeypatch):
    fake = _FakePygame()
    monkeypatch.setitem(sys.modules, 'pygame', fake)
    yield fake
    sys.modules.pop('pygame', None)


def test_esc_clears_active_tool(fake_pygame):
    ctrl = _Ctrl()
    ctrl.model.active_tool = 'sets_list'
    ev = SimpleNamespace(type=fake_pygame.KEYDOWN, key=fake_pygame.K_ESCAPE)

    h = mod.FsmToolbarEventHandler()
    consumed = h.handle_event(ctrl, ev)

    assert consumed is True
    assert ctrl.model.active_tool is None


def test_key_s_toggles_sets_list(fake_pygame):
    ctrl = _Ctrl()
    h = mod.FsmToolbarEventHandler()

    ev = SimpleNamespace(type=fake_pygame.KEYDOWN, key=fake_pygame.K_s)
    assert h.handle_event(ctrl, ev) is True
    assert ctrl.model.active_tool == 'sets_list'

    # Toggle off
    assert h.handle_event(ctrl, ev) is True
    assert ctrl.model.active_tool is None


def test_mousewheel_over_toolbar_consumed(fake_pygame):
    ctrl = _Ctrl()
    fake_pygame.mouse.set_pos((5, 5))
    ev = SimpleNamespace(type=fake_pygame.MOUSEWHEEL)

    h = mod.FsmToolbarEventHandler()
    assert h.handle_event(ctrl, ev) is True


def test_click_icon_toggles_tool(fake_pygame):
    ctrl = _Ctrl()
    h = mod.FsmToolbarEventHandler()

    # Click inside icon rect
    ev = SimpleNamespace(type=fake_pygame.MOUSEBUTTONDOWN, button=1, pos=(12, 12))
    assert h.handle_event(ctrl, ev) is True
    assert ctrl.model.active_tool == 'sets_list'

    # Click background inside panel (not on icon) -> consumed, no change
    ev_bg = SimpleNamespace(type=fake_pygame.MOUSEBUTTONDOWN, button=1, pos=(190, 10))
    assert h.handle_event(ctrl, ev_bg) is True
    assert ctrl.model.active_tool == 'sets_list'

    # RMB inside panel -> should return False to allow drag
    ev_rmb = SimpleNamespace(type=fake_pygame.MOUSEBUTTONDOWN, button=3, pos=(10, 10))
    assert h.handle_event(ctrl, ev_rmb) is False
