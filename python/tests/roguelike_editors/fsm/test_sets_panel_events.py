from __future__ import annotations

from types import SimpleNamespace

import pygame
import pytest

from roguelike_editors.fsm.fsm_sets_panel.fsm_sets_panel_events import FsmSetsPanelEventHandler


class _Ctrl:
    def __init__(self):
        self.model = SimpleNamespace(visible=True, items=['A', 'B', 'C'], hovered_index=None,
                                     hovered_button_row=None, hovered_button_kind=None, selected_index=None)
        self.view = SimpleNamespace(panel_rect=pygame.Rect(10, 20, 220, 200), row_button_rects={})
        # child handlers
        self.clone_events = SimpleNamespace(handle_button_click=lambda controller, idx: False)
        self.delete_events = SimpleNamespace(handle_button_click=lambda controller, idx: False,
                                             handle_modal_event=lambda controller, ev: False)
        # delete flow state
        self.delete_model = SimpleNamespace(confirm_visible=False)
        self.delete_view = SimpleNamespace(confirm_yes_rect=None, confirm_no_rect=None)


@pytest.fixture()
def ctrl():
    return _Ctrl()


def test_hover_updates_index_and_button(ctrl):
    h = FsmSetsPanelEventHandler()
    rect = ctrl.view.panel_rect
    # Row index calculation: (y - top - 28) // 20
    idx = 1
    y = rect.top + 28 + idx * 20 + 10
    x = rect.left + 150
    # Place clone button under cursor for row 1
    ctrl.view.row_button_rects = {idx: {'clone': pygame.Rect(x - 5, y - 5, 16, 16)}}

    ev = pygame.event.Event(pygame.MOUSEMOTION, {'pos': (x, y)})
    consumed = h.handle_event(ctrl, ev)

    assert consumed is True
    assert ctrl.model.hovered_index == idx
    assert ctrl.model.hovered_button_row == idx
    assert ctrl.model.hovered_button_kind == 'clone'


def test_click_row_selects_index(ctrl):
    h = FsmSetsPanelEventHandler()
    rect = ctrl.view.panel_rect
    idx = 2
    y = rect.top + 28 + idx * 20 + 10
    x = rect.left + 40  # not over any button

    ev = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {'pos': (x, y), 'button': 1})
    consumed = h.handle_event(ctrl, ev)

    assert consumed is True
    assert ctrl.model.selected_index == idx


def test_click_clone_delegates(ctrl):
    called = {}
    def _clone(controller, idx):
        called['idx'] = idx
        return True
    ctrl.clone_events = SimpleNamespace(handle_button_click=_clone)

    h = FsmSetsPanelEventHandler()
    rect = ctrl.view.panel_rect
    idx = 0
    y = rect.top + 28 + idx * 20 + 10
    x = rect.left + 180
    ctrl.view.row_button_rects = {idx: {'clone': pygame.Rect(x - 5, y - 5, 20, 16)}}

    ev = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {'pos': (x, y), 'button': 1})
    assert h.handle_event(ctrl, ev) is True
    assert called['idx'] == 0
    # selection should not change from None
    assert ctrl.model.selected_index is None


def test_click_delete_delegates(ctrl):
    called = {}
    def _delete(controller, idx):
        called['idx'] = idx
        return True
    ctrl.delete_events = SimpleNamespace(handle_button_click=_delete,
                                         handle_modal_event=lambda controller, ev: False)

    h = FsmSetsPanelEventHandler()
    rect = ctrl.view.panel_rect
    idx = 1
    y = rect.top + 28 + idx * 20 + 10
    x = rect.left + 200
    ctrl.view.row_button_rects = {idx: {'delete': pygame.Rect(x - 5, y - 5, 20, 16)}}

    ev = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {'pos': (x, y), 'button': 1})
    assert h.handle_event(ctrl, ev) is True
    assert called['idx'] == 1


def test_wheel_over_panel_is_consumed(ctrl):
    h = FsmSetsPanelEventHandler()
    rect = ctrl.view.panel_rect
    # Provide explicit pos inside panel to avoid relying on global mouse state
    pos = (rect.left + 5, rect.top + 5)
    ev = pygame.event.Event(pygame.MOUSEWHEEL, {'y': 1, 'pos': pos})
    assert h.handle_event(ctrl, ev) is True


def test_modal_delegation(ctrl):
    # When modal visible, delegate everything to delete_events.handle_modal_event
    got = {'called': 0}
    ctrl.delete_model.confirm_visible = True
    ctrl.delete_events = SimpleNamespace(handle_modal_event=lambda controller, ev: got.__setitem__('called', got['called'] + 1) or True,
                                         handle_button_click=lambda controller, idx: False)

    h = FsmSetsPanelEventHandler()
    ev = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {'button': 1, 'pos': (ctrl.view.panel_rect.left + 2, ctrl.view.panel_rect.top + 2)})
    assert h.handle_event(ctrl, ev) is True
    assert got['called'] == 1
