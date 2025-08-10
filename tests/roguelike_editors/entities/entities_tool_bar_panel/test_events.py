import pygame
from types import SimpleNamespace

from roguelike_editors.entities.entities_tool_bar_panel.entities_tool_bar_panel_events import (
    EntitiesToolBarPanelEventHandler,
)
from roguelike_editors.entities.services.constants import ENTITIES_TOOL_ON_MAP


def _make_toolbar_controller_stub():
    # Icon rects for three buttons
    icon_rects = {
        'undo': pygame.Rect(0, 0, 24, 24),
        'redo': pygame.Rect(30, 0, 24, 24),
        ENTITIES_TOOL_ON_MAP: pygame.Rect(60, 0, 24, 24),
    }

    undo_calls = {"undo": 0, "redo": 0}

    history = SimpleNamespace(
        undo=lambda: undo_calls.__setitem__("undo", undo_calls["undo"] + 1),
        redo=lambda: undo_calls.__setitem__("redo", undo_calls["redo"] + 1),
    )

    # widget with icon rects
    widget = SimpleNamespace(icon_rects=icon_rects)
    toolbar_view = SimpleNamespace(widget=widget)

    # model
    model = SimpleNamespace(active_tool=None, tools=[ENTITIES_TOOL_ON_MAP, 'undo', 'redo'])

    # picker visibility and editor visibility
    picker_model = SimpleNamespace(visible=False)
    picker_controller = SimpleNamespace(model=picker_model)
    editor_model = SimpleNamespace(visible=False)

    controller = SimpleNamespace(
        toolbar_view=toolbar_view,
        model=editor_model,
        picker_controller=picker_controller,
        history=history,
    )

    return controller, model, undo_calls


def test_undo_and_redo_clicks_invoke_history_methods():
    controller, model, calls = _make_toolbar_controller_stub()
    handler = EntitiesToolBarPanelEventHandler(controller, model)

    # Click undo
    ev_undo = SimpleNamespace(type=pygame.MOUSEBUTTONDOWN, button=1, pos=(5, 5))
    assert handler.handle_event(ev_undo) is True
    # Click redo
    ev_redo = SimpleNamespace(type=pygame.MOUSEBUTTONDOWN, button=1, pos=(35, 5))
    assert handler.handle_event(ev_redo) is True

    assert calls["undo"] == 1
    assert calls["redo"] == 1


def test_toggle_entities_on_map_tool_and_picker_visibility():
    controller, model, _ = _make_toolbar_controller_stub()
    handler = EntitiesToolBarPanelEventHandler(controller, model)

    # Initially inactive
    assert model.active_tool is None
    assert controller.model.visible is False
    assert controller.picker_controller.model.visible is False

    # Activate by clicking icon
    ev_on = SimpleNamespace(type=pygame.MOUSEBUTTONDOWN, button=1, pos=(65, 5))
    assert handler.handle_event(ev_on) is True
    assert model.active_tool == ENTITIES_TOOL_ON_MAP
    assert controller.model.visible is True
    assert controller.picker_controller.model.visible is True

    # Deactivate by clicking again
    ev_off = SimpleNamespace(type=pygame.MOUSEBUTTONDOWN, button=1, pos=(65, 5))
    assert handler.handle_event(ev_off) is True
    assert model.active_tool is None
    assert controller.picker_controller.model.visible is False
