import pytest
import pygame
from types import SimpleNamespace

from roguelike_editors.inventory.left_panel.list.list_event_handler import ListEventHandler

@pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    pygame.font.init()
    yield
    pygame.quit()

class DummyController:
    def __init__(self, items):
        self.items = items
        self.changed = None

    def get_items_list(self):
        return self.items

    def select_entity(self, eid):
        self.changed = eid

class DummyEditorController:
    def __init__(self, model):
        self.model = model
        # stub camera update
        self.game = SimpleNamespace(camera=SimpleNamespace(update=lambda target: setattr(self.model, 'camera_updated', True)))


def make_view(panel_rect, font=None, scroll_offset=0):
    if font is None:
        font = SimpleNamespace(get_linesize=lambda: 10)
    list_view = SimpleNamespace(scroll_panel=SimpleNamespace(scroll_offset=scroll_offset))
    return SimpleNamespace(panel_rect=panel_rect, font=font, list_view=list_view)


def test_click_outside():
    model = SimpleNamespace(current_category='hostile', editing_side=None)
    ec = DummyEditorController(model)
    controller = DummyController([])
    view = make_view(pygame.Rect(0, 0, 10, 10))
    handler = ListEventHandler(ec, controller, view, model)
    event = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {'button': 1, 'pos': (20, 20)})
    assert handler.handle(event) is False


def test_click_inside_non_monsters():
    model = SimpleNamespace(current_category='player', editing_side=None)
    ec = DummyEditorController(model)
    controller = DummyController([])
    view = make_view(pygame.Rect(0, 0, 10, 10))
    handler = ListEventHandler(ec, controller, view, model)
    event = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {'button': 1, 'pos': (5, 5)})
    result = handler.handle(event)
    assert result is True
    assert controller.changed is None
    assert model.editing_side is None


def test_single_click_monsters():
    model = SimpleNamespace(current_category='hostile', editing_side=None)
    ec = DummyEditorController(model)
    items = ['E1', '  Item']
    controller = DummyController(items)
    view = make_view(pygame.Rect(0, 0, 100, 100))
    handler = ListEventHandler(ec, controller, view, model)
    # click on first line (idx=0)
    event = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {'button': 1, 'pos': (0, 5)})
    result = handler.handle(event)
    assert result is True
    assert controller.changed == 'E1'
    assert model.editing_side == 'active'


def test_double_click_pos_monsters(monkeypatch):
    model = SimpleNamespace(current_category='hostile', editing_side=None, camera_focus_target=None)
    ec = DummyEditorController(model)
    items = ['E1', '  Pos: (3.0,4.0)']
    controller = DummyController(items)
    view = make_view(pygame.Rect(0, 0, 100, 100))
    handler = ListEventHandler(ec, controller, view, model)
    # simulate prior click
    handler.last_pos_click_time = 100
    handler.last_pos_click_idx = 1
    monkeypatch.setattr(pygame.time, 'get_ticks', lambda: 150)
    # click on second line (idx=1)
    event = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {'button': 1, 'pos': (0, 10)})
    result = handler.handle(event)
    assert result is True
    # camera update
    assert hasattr(model, 'camera_focus_target')
    target = model.camera_focus_target
    assert pytest.approx(target.x) == 3.0 and pytest.approx(target.y) == 4.0
    # entity selection
    assert controller.changed == 'E1'
    assert model.editing_side == 'active'
