import pygame
import pytest

from roguelike_editors.entities.entities_properties_panel.entities_assets_grid_panel.entities_assets_grid_panel_events import AssetsGridPanelEventHandler
from roguelike_editors.entities.entities_properties_panel.entities_assets_grid_panel.entities_assets_grid_panel_model import AssetsGridPanelModel


class DummyController:
    def __init__(self):
        self.model = AssetsGridPanelModel()
        self.view = None


def test_hover_sets_hovered_asset_cell():
    ctrl = DummyController()
    handler = AssetsGridPanelEventHandler(ctrl)
    rect1 = pygame.Rect(10, 10, 50, 50)
    key1 = 'asset1'
    ctrl.model.asset_cell_entries = [(rect1, key1)]
    event = pygame.event.Event(pygame.MOUSEMOTION, pos=(20, 20))
    consumed = handler.handle(event)
    assert consumed is True
    assert ctrl.model.hovered_asset_cell == key1


def test_hover_outside_clears_hovered_asset_cell():
    ctrl = DummyController()
    handler = AssetsGridPanelEventHandler(ctrl)
    rect1 = pygame.Rect(10, 10, 50, 50)
    key1 = 'asset1'
    ctrl.model.asset_cell_entries = [(rect1, key1)]
    event = pygame.event.Event(pygame.MOUSEMOTION, pos=(0, 0))
    consumed = handler.handle(event)
    assert consumed is False
    assert ctrl.model.hovered_asset_cell is None


def test_click_sets_selected_asset_cell():
    ctrl = DummyController()
    handler = AssetsGridPanelEventHandler(ctrl)
    rect1 = pygame.Rect(10, 10, 50, 50)
    key1 = 'asset1'
    ctrl.model.asset_cell_entries = [(rect1, key1)]
    event = pygame.event.Event(pygame.MOUSEBUTTONDOWN, pos=(20, 20), button=1)
    consumed = handler.handle(event)
    assert consumed is True
    assert ctrl.model.selected_asset_cell == key1


def test_click_outside_does_not_set_selected_asset_cell():
    ctrl = DummyController()
    handler = AssetsGridPanelEventHandler(ctrl)
    rect1 = pygame.Rect(10, 10, 50, 50)
    key1 = 'asset1'
    ctrl.model.asset_cell_entries = [(rect1, key1)]
    event = pygame.event.Event(pygame.MOUSEBUTTONDOWN, pos=(0, 0), button=1)
    consumed = handler.handle(event)
    assert consumed is False
    assert ctrl.model.selected_asset_cell is None


def test_non_relevant_event_returns_false():
    ctrl = DummyController()
    handler = AssetsGridPanelEventHandler(ctrl)
    event = pygame.event.Event(pygame.KEYDOWN, key=pygame.K_a)
    consumed = handler.handle(event)
    assert consumed is False
