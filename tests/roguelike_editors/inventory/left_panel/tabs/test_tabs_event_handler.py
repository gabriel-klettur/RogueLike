import pygame
from roguelike_editors.inventory.left_panel.tabs.tabs_event_handler import TabsEventHandler


class DummyController:
    def __init__(self):
        self.changed = None

    def change_category(self, cat):
        self.changed = cat


class DummyView:
    def __init__(self, rects):
        self.tab_rects = rects


def test_handle_tab_click():
    pygame.init()
    rect1 = pygame.Rect(0, 0, 10, 10)
    controller = DummyController()
    view = DummyView([(rect1, 'player')])
    handler = TabsEventHandler(None, controller, view, None)
    event = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {'button': 1, 'pos': (5, 5)})
    result = handler.handle(event)
    assert result is True
    assert controller.changed == 'player'


def test_handle_click_outside():
    pygame.init()
    rect = pygame.Rect(0, 0, 10, 10)
    controller = DummyController()
    view = DummyView([(rect, 'monsters')])
    handler = TabsEventHandler(None, controller, view, None)
    event = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {'button': 1, 'pos': (20, 20)})
    result = handler.handle(event)
    assert result is False
    assert controller.changed is None


def test_handle_non_left_click():
    pygame.init()
    rect = pygame.Rect(0, 0, 10, 10)
    controller = DummyController()
    view = DummyView([(rect, 'map')])
    handler = TabsEventHandler(None, controller, view, None)
    event = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {'button': 3, 'pos': (5, 5)})
    result = handler.handle(event)
    assert result is False
    assert controller.changed is None
