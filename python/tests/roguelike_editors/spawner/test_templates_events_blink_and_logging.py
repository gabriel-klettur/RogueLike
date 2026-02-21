import pygame
import pytest

from roguelike_editors.spawner.spawner_templates_panel.list_templates.list_templates_view import ListTemplatesView
from roguelike_editors.spawner.spawner_templates_panel.list_templates.list_templates_events import ListTemplatesEventHandler


class DummyController:
    def __init__(self, model, view):
        self.model = model
        self.view = view
        self.add_calls = []
        self.clone_calls = []
        self.delete_calls = []
        self.delete_events = type('Del', (), {'handle_button_click': lambda *_: True})()

    def add_template_at(self, index: int) -> None:
        self.add_calls.append(index)

    def clone_template_at(self, index: int) -> None:
        self.clone_calls.append(index)


def make_model(items, **over):
    base = {
        'visible': True,
        'title': 'Templates',
        'panel_width': 360,
        'header_height': 28,
        'row_height': 20,
        'visible_rows': 5,
        'scroll_offset': 0,
        'items': items,
        'selected_index': None,
    }
    base.update(over)
    return type('M', (), base)()


@pytest.fixture(autouse=True)
def _clean_events():
    yield
    pygame.event.clear()


def test_click_add_sets_blink_and_logs(caplog):
    screen = pygame.Surface((800, 600))
    view = ListTemplatesView()
    model = make_model(['valeria'])
    ctrl = DummyController(model, view)
    events = ListTemplatesEventHandler()

    rect = view.render(model, screen, anchor=(10, 10))
    assert rect is not None
    btns = view.row_button_rects
    assert len(btns) == 1
    add_rect = btns[0]['add']

    caplog.clear()
    caplog.set_level('DEBUG')
    pos = add_rect.center
    ev = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {'pos': pos, 'button': 1})
    handled = events.handle_event(ctrl, ev)

    assert handled is True
    assert ctrl.add_calls == [0]
    assert getattr(model, '_blink_row_index', None) == 0
    assert isinstance(getattr(model, '_blink_end_ticks', 0), int)
    assert any("'+' clicked" in rec.message for rec in caplog.records)


def test_click_clone_sets_blink_and_calls_handler(caplog):
    screen = pygame.Surface((800, 600))
    view = ListTemplatesView()
    model = make_model(['valeria'])
    ctrl = DummyController(model, view)
    events = ListTemplatesEventHandler()

    view.render(model, screen, anchor=(10, 10))
    clone_rect = view.row_button_rects[0]['clone']

    caplog.clear()
    caplog.set_level('DEBUG')
    ev = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {'pos': clone_rect.center, 'button': 1})
    handled = events.handle_event(ctrl, ev)

    assert handled is True
    assert ctrl.clone_calls == [0]
    assert getattr(model, '_blink_row_index', None) == 0
    assert any("'⧉' clicked" in rec.message for rec in caplog.records)


def test_click_row_not_on_buttons_sets_selection_and_blink():
    screen = pygame.Surface((800, 600))
    view = ListTemplatesView()
    model = make_model(['valeria'])
    ctrl = DummyController(model, view)
    events = ListTemplatesEventHandler()

    rect = view.render(model, screen, anchor=(10, 10))
    assert rect is not None

    # Click near row text area (left side), away from buttons
    header_h = model.header_height
    row_h = model.row_height
    row_center_y = rect.top + header_h + row_h // 2
    text_x = rect.left + 20
    ev = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {'pos': (text_x, row_center_y), 'button': 1})

    handled = events.handle_event(ctrl, ev)
    assert handled is True
    assert model.selected_index == 0
    assert getattr(model, '_blink_row_index', None) == 0


def test_click_delete_sets_blink_and_invokes_modal(caplog):
    screen = pygame.Surface((800, 600))
    view = ListTemplatesView()
    model = make_model(['valeria'])
    ctrl = DummyController(model, view)
    events = ListTemplatesEventHandler()

    view.render(model, screen, anchor=(10, 10))
    del_rect = view.row_button_rects[0]['delete']

    caplog.clear()
    caplog.set_level('DEBUG')
    ev = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {'pos': del_rect.center, 'button': 1})
    handled = events.handle_event(ctrl, ev)

    assert handled is True
    assert getattr(model, '_blink_row_index', None) == 0
    assert any("'x' clicked" in rec.message for rec in caplog.records)
