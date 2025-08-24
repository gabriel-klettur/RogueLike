from __future__ import annotations

from types import SimpleNamespace

import pygame

from roguelike_editors.fsm.fsm_sets_panel.sets_panel_delete.sets_panel_delete_events import (
    SetsPanelDeleteEventHandler,
)


class _Ctrl:
    def __init__(self):
        # Parent panel surface/rect
        self.view = SimpleNamespace(panel_rect=pygame.Rect(10, 20, 240, 180))
        # Delete MVC
        self.delete_model = SimpleNamespace(confirm_visible=True)
        self.delete_view = SimpleNamespace(
            confirm_yes_rect=pygame.Rect(30, 40, 40, 20),
            confirm_no_rect=pygame.Rect(80, 40, 40, 20),
        )
        self.model = SimpleNamespace()
        # Controller methods to capture calls
        self.delete = SimpleNamespace(
            confirm_yes=lambda parent: setattr(self, '_yes', True),
            confirm_no=lambda parent: setattr(self, '_no', True),
        )


def test_modal_click_yes_calls_confirm_yes():
    c = _Ctrl()
    h = SetsPanelDeleteEventHandler()
    ev = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {'button': 1, 'pos': (35, 45)})
    assert h.handle_modal_event(c, ev) is True
    assert getattr(c, '_yes', False) is True
    assert getattr(c, '_no', False) is False


def test_modal_click_no_calls_confirm_no():
    c = _Ctrl()
    h = SetsPanelDeleteEventHandler()
    ev = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {'button': 1, 'pos': (85, 45)})
    assert h.handle_modal_event(c, ev) is True
    assert getattr(c, '_no', False) is True
    assert getattr(c, '_yes', False) is False


def test_modal_click_bg_inside_panel_consumes():
    c = _Ctrl()
    h = SetsPanelDeleteEventHandler()
    # Inside panel but not on buttons
    ev = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {'button': 1, 'pos': (150, 100)})
    assert h.handle_modal_event(c, ev) is True


def test_modal_key_return_and_y_confirm_yes():
    c = _Ctrl()
    h = SetsPanelDeleteEventHandler()
    ev_ret = pygame.event.Event(pygame.KEYDOWN, {'key': pygame.K_RETURN})
    assert h.handle_modal_event(c, ev_ret) is True
    assert getattr(c, '_yes', False) is True

    c2 = _Ctrl()
    ev_y = pygame.event.Event(pygame.KEYDOWN, {'key': pygame.K_y})
    assert h.handle_modal_event(c2, ev_y) is True
    assert getattr(c2, '_yes', False) is True


def test_modal_key_escape_and_n_confirm_no():
    c = _Ctrl()
    h = SetsPanelDeleteEventHandler()
    ev_esc = pygame.event.Event(pygame.KEYDOWN, {'key': pygame.K_ESCAPE})
    assert h.handle_modal_event(c, ev_esc) is True
    assert getattr(c, '_no', False) is True

    c2 = _Ctrl()
    ev_n = pygame.event.Event(pygame.KEYDOWN, {'key': pygame.K_n})
    assert h.handle_modal_event(c2, ev_n) is True
    assert getattr(c2, '_no', False) is True


def test_modal_outside_panel_not_consumed():
    c = _Ctrl()
    h = SetsPanelDeleteEventHandler()
    # Pos outside the panel
    ev = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {'button': 1, 'pos': (5, 5)})
    assert h.handle_modal_event(c, ev) is False
