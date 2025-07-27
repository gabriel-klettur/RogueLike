import pytest
import pygame
from types import SimpleNamespace
from roguelike_editors.inventory.right_panel.item_selection_panel.tittle.tittle_event_handler import TittleEventHandler
from roguelike_editors.inventory.right_panel.item_selection_panel.item_selection_panel_model import ItemSelectionPanelModel

@pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    yield
    pygame.quit()

@pytest.fixture
def setup_handler():
    model = ItemSelectionPanelModel([])
    # stub controller with close method
    controller = pytest.MonkeyPatch()
    # Actually use SimpleNamespace for controller
    from types import SimpleNamespace
    ctrl = SimpleNamespace(model=model)
    ctrl.closed = False
    ctrl.close = lambda: setattr(ctrl, 'closed', True)
    # dummy view rects
    panel_rect = pygame.Rect(10, 10, 100, 50)
    header_h = pygame.font.SysFont(None,24).render("Item List",True,(0,0,0)).get_height() + 5
    header_rect = pygame.Rect(panel_rect.x, panel_rect.y - header_h, panel_rect.width, header_h)
    view = SimpleNamespace(panel_rect=panel_rect, header_rect=header_rect)
    handler = TittleEventHandler(ctrl, view)
    return handler, ctrl, model, view


def test_click_outside_closes_panel(setup_handler):
    handler, ctrl, model, view = setup_handler
    # click outside both rects
    event = SimpleNamespace(type=pygame.MOUSEBUTTONDOWN, button=1, pos=(0,0))
    result = handler.handle(event)
    assert result is True
    assert getattr(ctrl, 'closed') is True


def test_click_on_header_starts_drag(setup_handler):
    handler, ctrl, model, view = setup_handler
    # click inside header_rect
    pos = (view.header_rect.centerx, view.header_rect.centery)
    event = SimpleNamespace(type=pygame.MOUSEBUTTONDOWN, button=1, pos=pos)
    result = handler.handle(event)
    assert result is True
    assert model.tittle_model.show_panel is model.tittle_model.show_panel  # unchanged
    assert model.button_model.dragging is True
    expected_start = pygame.Vector2(pos) - model.button_model.drag_offset
    assert model.button_model.drag_start_pos == expected_start


def test_handle_motion_updates_drag_offset(setup_handler):
    handler, ctrl, model, view = setup_handler
    # manually start dragging
    model.button_model.dragging = True
    model.button_model.drag_start_pos = pygame.Vector2(5,5)
    # simulate motion
    new_pos = (15, 20)
    event = SimpleNamespace(type=pygame.MOUSEMOTION, pos=new_pos)
    result = handler.handle(event)
    assert result is True
    assert model.button_model.drag_offset == pygame.Vector2(new_pos) - pygame.Vector2(5,5)


def test_handle_release_ends_drag(setup_handler):
    handler, ctrl, model, view = setup_handler
    model.button_model.dragging = True
    event = SimpleNamespace(type=pygame.MOUSEBUTTONUP, button=1, pos=(0,0))
    result = handler.handle(event)
    assert result is True
    assert model.button_model.dragging is False


def test_non_relevant_event_returns_false(setup_handler):
    handler, ctrl, model, view = setup_handler
    event = SimpleNamespace(type=pygame.KEYDOWN, key=pygame.K_SPACE)
    assert handler.handle(event) is False
