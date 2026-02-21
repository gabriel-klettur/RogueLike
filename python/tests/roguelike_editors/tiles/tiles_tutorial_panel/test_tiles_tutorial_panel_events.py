import pytest
import pygame
from types import SimpleNamespace

from roguelike_editors.tiles.tiles_tutorial_panel.tiles_tutorial_panel_events import (
    TilesTutorialPanelEventHandler,
)
from roguelike_editors.tiles.tiles_tutorial_panel.tiles_tutorial_panel_model import (
    TilesTutorialPanelModel,
)


@pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    yield
    pygame.quit()


@pytest.fixture
def handler_and_model():
    # Minimal controller; drag tests don't need controller methods
    controller = SimpleNamespace()
    model = TilesTutorialPanelModel()
    model.active = True
    # Define a panel rect at a known location
    model.panel_rect = pygame.Rect(100, 100, 240, 160)
    handler = TilesTutorialPanelEventHandler(controller, model)
    return handler, model


def test_right_click_start_drag_inside_panel_sets_dragging_and_offset(handler_and_model):
    handler, model = handler_and_model
    # model.pos is None initially; offset should be event.pos - panel_rect.topleft
    ev_down = pygame.event.Event(pygame.MOUSEBUTTONDOWN, button=3, pos=(110, 120))
    assert handler.handle(ev_down) is True
    assert model.dragging is True
    assert model.drag_offset == (10, 20)


def test_mouse_motion_while_dragging_updates_model_pos(handler_and_model):
    handler, model = handler_and_model
    # Start drag first
    ev_down = pygame.event.Event(pygame.MOUSEBUTTONDOWN, button=3, pos=(150, 180))
    assert handler.handle(ev_down) is True
    # Move mouse; new pos = mouse - offset
    ev_move = pygame.event.Event(pygame.MOUSEMOTION, pos=(200, 230))
    assert handler.handle(ev_move) is True
    assert model.pos == (200 - (150 - 100), 230 - (180 - 100))  # (150, 150)


def test_right_button_up_stops_drag(handler_and_model):
    handler, model = handler_and_model
    # Start drag
    ev_down = pygame.event.Event(pygame.MOUSEBUTTONDOWN, button=3, pos=(120, 130))
    assert handler.handle(ev_down) is True
    assert model.dragging is True
    # Release
    ev_up = pygame.event.Event(pygame.MOUSEBUTTONUP, button=3, pos=(120, 130))
    assert handler.handle(ev_up) is True
    assert model.dragging is False


def test_inactive_model_does_not_handle_drag():
    controller = SimpleNamespace()
    model = TilesTutorialPanelModel()
    model.active = False
    model.panel_rect = pygame.Rect(100, 100, 240, 160)
    handler = TilesTutorialPanelEventHandler(controller, model)
    ev_down = pygame.event.Event(pygame.MOUSEBUTTONDOWN, button=3, pos=(110, 120))
    assert handler.handle(ev_down) is False
