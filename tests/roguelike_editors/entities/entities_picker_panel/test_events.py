import pygame
from types import SimpleNamespace

from roguelike_editors.entities.entities_picker_panel.entities_picker_panel_events import (
    EntitiesPickerEventHandler,
)


def _make_picker_controller_stub():
    # Tabs
    tabs = {
        "Players": pygame.Rect(0, 0, 80, 20),
        "Hostile": pygame.Rect(80, 0, 100, 20),
    }
    model = SimpleNamespace(
        visible=True,
        tab_rects=tabs,
        active_tab="Hostile",
        scroll_index=0,
        hovered_id=None,
        selected_id=None,
        panel_rect=pygame.Rect(0, 0, 300, 300),
        monsters={"m1": {}, "m2": {}},
        player_stats={"p1": {}, "p2": {}},
    )

    calls = {"drag_events": []}

    def drag_handle_event(ev, header_rect=None):
        # Record the event and mark dragging appropriately
        if getattr(ev, "type", None) == pygame.MOUSEBUTTONDOWN and getattr(ev, "button", None) == 3:
            draggable.dragging = True
        elif ev.type == pygame.MOUSEBUTTONUP:
            draggable.dragging = False
        calls["drag_events"].append(ev.type)

    draggable = SimpleNamespace(dragging=False, handle_event=drag_handle_event)

    font = pygame.font.Font(None, 14)
    view = SimpleNamespace(
        x=0,
        y=0,
        margin=4,
        cell_size=32,
        text_margin=2,
        font=font,
        columns=2,
        draggable_panel=draggable,
    )

    controller = SimpleNamespace(model=model, view=view)
    return controller, calls


def test_tab_click_switches_active_tab_and_resets_state():
    controller, _ = _make_picker_controller_stub()
    handler = EntitiesPickerEventHandler(controller)

    # Click inside Players tab
    ev = SimpleNamespace(type=pygame.MOUSEBUTTONDOWN, button=1, pos=(10, 10))
    consumed = handler.handle(ev)

    assert consumed is True
    assert controller.model.active_tab == "Players"
    assert controller.model.scroll_index == 0
    assert controller.model.hovered_id is None
    assert controller.model.selected_id is None


def test_grid_click_selects_entity_id():
    controller, _ = _make_picker_controller_stub()
    handler = EntitiesPickerEventHandler(controller)

    # Position over first cell of Hostile grid
    margin = controller.view.margin
    header_h = next(iter(controller.model.tab_rects.values())).height
    x = controller.view.x + margin + 5
    y = controller.view.y + margin + header_h + 5
    ev = SimpleNamespace(type=pygame.MOUSEBUTTONDOWN, button=1, pos=(x, y))

    handler.handle(ev)
    assert controller.model.selected_id == "m1"


def test_hover_sets_hovered_id():
    controller, _ = _make_picker_controller_stub()
    handler = EntitiesPickerEventHandler(controller)

    # Hover over second cell in row 0, col 1
    margin = controller.view.margin
    header_h = next(iter(controller.model.tab_rects.values())).height
    x = controller.view.x + margin + controller.view.cell_size + margin + 5
    y = controller.view.y + margin + header_h + 5

    ev = SimpleNamespace(type=pygame.MOUSEMOTION, pos=(x, y))
    consumed = handler.handle(ev)

    assert consumed is True
    assert controller.model.hovered_id == "m2"


def test_drag_sequence_right_button_moves_panel():
    controller, calls = _make_picker_controller_stub()
    handler = EntitiesPickerEventHandler(controller)

    # Right mouse down inside panel
    down = SimpleNamespace(type=pygame.MOUSEBUTTONDOWN, button=3, pos=(10, 10))
    assert handler.handle(down) is True
    assert controller.view.draggable_panel.dragging is True

    # Motion while dragging
    motion = SimpleNamespace(type=pygame.MOUSEMOTION, pos=(20, 20), rel=(10, 10))
    assert handler.handle(motion) is True

    # Release
    up = SimpleNamespace(type=pygame.MOUSEBUTTONUP, button=3, pos=(20, 20))
    assert handler.handle(up) is True
    assert controller.view.draggable_panel.dragging is False

    # Sanity: events recorded
    assert calls["drag_events"] == [pygame.MOUSEBUTTONDOWN, pygame.MOUSEMOTION, pygame.MOUSEBUTTONUP]


def test_keydown_up_down_only():
    controller, _ = _make_picker_controller_stub()
    handler = EntitiesPickerEventHandler(controller)

    # DOWN increases scroll_index
    ev_down = SimpleNamespace(type=pygame.KEYDOWN, key=pygame.K_DOWN)
    assert handler.handle(ev_down) is True
    assert controller.model.scroll_index == 1

    # UP decreases but not below 0
    ev_up = SimpleNamespace(type=pygame.KEYDOWN, key=pygame.K_UP)
    assert handler.handle(ev_up) is True
    assert controller.model.scroll_index == 0

    # F5 no longer toggles locally (centralized); handler still consumes keydown path
    ev_f5 = SimpleNamespace(type=pygame.KEYDOWN, key=pygame.K_F5)
    assert handler.handle(ev_f5) is True
    # Visibility unchanged
    assert controller.model.visible is True


def test_click_outside_panel_returns_false():
    controller, _ = _make_picker_controller_stub()
    handler = EntitiesPickerEventHandler(controller)

    # Click outside panel bounds
    ev = SimpleNamespace(type=pygame.MOUSEBUTTONDOWN, button=1, pos=(400, 400))
    assert handler.handle(ev) is False  # outside panel and tabs -> handler should not consume
