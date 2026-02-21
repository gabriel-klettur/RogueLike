import pygame
import pytest
from types import SimpleNamespace

from roguelike_editors.entities.entities_tool_bar_panel.entities_tool_bar_panel_events import (
    EntitiesToolBarPanelEventHandler,
)


@pytest.fixture(autouse=True)
def _init_pygame():
    # Headless init is handled by root conftest, this ensures pygame is available per-test
    pygame.init()
    yield
    pygame.quit()


@pytest.fixture()
def handler_with_history_stub():
    # History stub to capture calls
    calls = {"undo": 0, "redo": 0}

    class HistoryStub:
        def undo(self):
            calls["undo"] += 1
            return True

        def redo(self):
            calls["redo"] += 1
            return True

    # Minimal controller with icon_rects in toolbar_view.widget
    icon_rects = {}
    widget = SimpleNamespace(icon_rects=icon_rects)
    toolbar_view = SimpleNamespace(widget=widget)

    controller = SimpleNamespace(
        toolbar_view=toolbar_view,
        history=HistoryStub(),
        # Attributes unused for undo/redo path but present on controller in real app
        picker_controller=SimpleNamespace(model=SimpleNamespace(visible=False)),
        model=SimpleNamespace(visible=True),
    )
    model = SimpleNamespace(active_tool=None)
    handler = EntitiesToolBarPanelEventHandler(controller, model)
    return handler, controller, calls


def test_undo_button_click_invokes_history_undo(handler_with_history_stub):
    handler, controller, calls = handler_with_history_stub
    # Place an 'undo' icon at (0,0)-(10,10)
    controller.toolbar_view.widget.icon_rects["undo"] = pygame.Rect(0, 0, 10, 10)
    # Left click inside the rect
    ev = SimpleNamespace(type=pygame.MOUSEBUTTONDOWN, button=1, pos=(5, 5))

    consumed = handler.handle_event(ev)

    assert consumed is True
    assert calls["undo"] == 1
    assert calls["redo"] == 0


def test_redo_button_click_invokes_history_redo(handler_with_history_stub):
    handler, controller, calls = handler_with_history_stub
    # Place a 'redo' icon at (0,0)-(10,10)
    controller.toolbar_view.widget.icon_rects["redo"] = pygame.Rect(0, 0, 10, 10)
    # Left click inside the rect
    ev = SimpleNamespace(type=pygame.MOUSEBUTTONDOWN, button=1, pos=(5, 5))

    consumed = handler.handle_event(ev)

    assert consumed is True
    assert calls["redo"] == 1
    assert calls["undo"] == 0


def test_click_outside_undo_redo_returns_false(handler_with_history_stub):
    handler, controller, calls = handler_with_history_stub
    # Define both rects but click outside
    controller.toolbar_view.widget.icon_rects["undo"] = pygame.Rect(0, 0, 10, 10)
    controller.toolbar_view.widget.icon_rects["redo"] = pygame.Rect(20, 20, 10, 10)

    ev = SimpleNamespace(type=pygame.MOUSEBUTTONDOWN, button=1, pos=(15, 15))

    consumed = handler.handle_event(ev)

    assert consumed is False
    assert calls["undo"] == 0 and calls["redo"] == 0
