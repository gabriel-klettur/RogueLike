import pygame
import pytest
from roguelike_editors.entities.entities_properties_panel.entities_properties_panel_events import EntitiesPropertiesPanelEventHandler
from roguelike_editors.entities.entities_properties_panel.entities_properties_panel_events import EntitiesPropertiesPanelEventHandler
from roguelike_editors.entities.entities_properties_panel.entities_properties_panel_events import EntitiesPropertiesPanelEventHandler

from roguelike_editors.entities.entities_properties_panel.entities_properties_panel_events import EntitiesPropertiesPanelEventHandler
from roguelike_editors.entities.entities_properties_panel.entities_properties_panel_model import EntityPropertiesPanelModel


class DummyView:
    def __init__(self):
        # font with get_height
        self.font = type('F', (), {'get_height': lambda self: 10})()
        # draggable_panel stub
        self.draggable_panel = type('DP', (), {'dragging': False, 'handle_event': lambda self, e, header_rect=None: None})()


class DummyController:
    def __init__(self):
        self.model = EntityPropertiesPanelModel(player_stats={}, player_assets={}, monsters={})
        self.model.selected_id = 'dummy'
        self.view = DummyView()
        # assets picker not visible
        self.assets_picker_controller = type('APC', (), {'model': type('M', (), {'visible': False}), 'handle_event': lambda self, e: False})()
        # type_assets_controller with active tab
        self.type_assets_controller = type('TAC', (), {'model': type('M', (), {'active_type_tab': 'properties'}), 'handle_event': lambda self, e: False})()
        self.state_tabs_controller = type('STC', (), {'handle_event': lambda self, e: False})()
        self.set_ot_assets_tab_controller = type('SOTAC', (), {'handle_event': lambda self, e: False})()
        self.grid_controller = type('GC', (), {'handle_event': lambda self, e: False})()


def test_hover_properties_sets_hovered_property():
    ctrl = DummyController()
    handler = EntitiesPropertiesPanelEventHandler(ctrl)
    # set panel rect and property entries
    ctrl.model.panel_rect = pygame.Rect(0, 0, 100, 100)
    rect1 = pygame.Rect(10, 10, 50, 20)
    key1 = 'prop1'
    ctrl.model.property_entries = [(rect1, key1)]
    # simulate motion inside
    event = pygame.event.Event(pygame.MOUSEMOTION, pos=(20, 20))
    handler.handle(event)
    assert ctrl.model.hovered_property == key1


def test_hover_properties_outside_returns_false():
    ctrl = DummyController()
    handler = EntitiesPropertiesPanelEventHandler(ctrl)
    ctrl.model.panel_rect = pygame.Rect(0, 0, 100, 100)
    rect1 = pygame.Rect(10, 10, 50, 20)
    key1 = 'prop1'
    ctrl.model.property_entries = [(rect1, key1)]
    # simulate motion outside
    event = pygame.event.Event(pygame.MOUSEMOTION, pos=(200, 200))
    consumed = handler.handle(event)
    assert consumed is False
    assert ctrl.model.hovered_property is None


def test_hover_skipped_on_assets_tab():
    ctrl = DummyController()
    # switch to assets tab
    ctrl.type_assets_controller.model.active_type_tab = 'assets'
    handler = EntitiesPropertiesPanelEventHandler(ctrl)
    ctrl.model.panel_rect = pygame.Rect(0, 0, 100, 100)
    rect1 = pygame.Rect(10, 10, 50, 20)
    key1 = 'prop1'
    ctrl.model.property_entries = [(rect1, key1)]
    # simulate motion inside
    event = pygame.event.Event(pygame.MOUSEMOTION, pos=(20, 20))
    consumed = handler.handle(event)
    assert consumed is False
    assert ctrl.model.hovered_property is None


def test_non_relevant_event_returns_false():
    ctrl = DummyController()
    handler = EntitiesPropertiesPanelEventHandler(ctrl)
    event = pygame.event.Event(pygame.KEYDOWN, key=pygame.K_a)
    consumed = handler.handle(event)
    assert consumed is False
