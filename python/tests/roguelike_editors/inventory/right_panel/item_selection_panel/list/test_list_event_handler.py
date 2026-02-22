import pytest
import pygame
from types import SimpleNamespace
from roguelike_editors.inventory.right_panel.item_selection_panel.list.list_event_handler import ListEventHandler

@pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    yield
    pygame.quit()

@pytest.fixture
def setup_handler():
    # Fake scroll panel with handle_event based on event.handled attr
    scroll_panel = SimpleNamespace(items=['a', 'b'], scroll_offset=0,
                                    handle_event=lambda e: getattr(e, 'handled', False))
    font = SimpleNamespace(get_linesize=lambda: 10)
    view = SimpleNamespace(scroll_panel=scroll_panel,
                            panel_rect=pygame.Rect(0, 0, 100, 100),
                            margin=2,
                            font=font)
    model = SimpleNamespace(visible_count=10)
    controller = SimpleNamespace(model=model)
    # Attach list_controller stub
    controller.list_controller = SimpleNamespace(select_item=lambda item, idx: setattr(model, 'sel', (item, idx)))
    handler = ListEventHandler(controller, view)
    return handler, controller, view


def test_scroll_event_handled(setup_handler):
    handler, _, _ = setup_handler
    event = SimpleNamespace(type=pygame.MOUSEBUTTONDOWN, handled=True)
    assert handler.handle(event) is True


def test_scroll_event_not_handled(setup_handler):
    handler, _, _ = setup_handler
    event = SimpleNamespace(type=pygame.MOUSEWHEEL, handled=False)
    assert handler.handle(event) is False


def test_click_inside_selects_item(setup_handler):
    handler, controller, view = setup_handler
    # Ensure scroll events not consumed
    view.scroll_panel.handle_event = lambda e: False
    # Calculate a click position inside first item (index 0)
    # scroll_rect.y = panel_rect.y + (line_h + margin) = 0 + 12
    # first item y range: y0 + idx*line_h: so pos y = 12 + view.margin + 0*10 = 14
    pos = (5, 14)
    event = SimpleNamespace(type=pygame.MOUSEBUTTONUP, button=1, pos=pos)
    result = handler.handle(event)
    assert result is True
    assert controller.model.sel == ('a', 0)


def test_click_outside_returns_false(setup_handler):
    handler, _, view = setup_handler
    view.scroll_panel.handle_event = lambda e: False
    event = SimpleNamespace(type=pygame.MOUSEBUTTONUP, button=1, pos=(0, 0))
    assert handler.handle(event) is False
