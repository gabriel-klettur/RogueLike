import pytest
import pygame
from types import SimpleNamespace

from roguelike_editors.inventory.left_panel.panel_event_handler import PanelEventHandler

@pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    pygame.font.init()
    yield
    pygame.quit()

class DummyCamera:
    def __init__(self):
        self.updated = None
    def update(self, target):
        self.updated = target

class DummyEditorController:
    def __init__(self, model, world, camera):
        self.model = model
        self.world = world
        self.game = SimpleNamespace(camera=camera)

@pytest.fixture
def base_setup():
    # model with focus target
    model = SimpleNamespace(camera_focus_target='focus')
    # world with player position
    pos = SimpleNamespace(x=5, y=6)
    world = SimpleNamespace(player_entity='p1', components={'Position': {'p1': pos}})
    camera = DummyCamera()
    ec = DummyEditorController(model, world, camera)
    # controller and view stubs
    controller = SimpleNamespace()
    view = SimpleNamespace(tab_rects=[(pygame.Rect(0, 0, 10, 10), 'a')], panel_rect=pygame.Rect(100, 100, 10, 10))
    handler = PanelEventHandler(ec, controller, view, model)
    return handler, ec, view, model

def test_camera_recenter_and_focus_cleared(base_setup):
    handler, ec, view, model = base_setup
    # stub handlers to prevent early return
    handler.tabs_handler.handle = lambda ev: False
    handler.list_handler.handle = lambda ev: False
    event = pygame.event.Event(pygame.MOUSEBUTTONDOWN, {})
    result = handler.handle(event)
    # camera updated to player pos
    assert ec.game.camera.updated.x == 5 and ec.game.camera.updated.y == 6
    # focus target cleared
    assert model.camera_focus_target is None
    assert result is False

def test_tabs_handler_short_circuit(base_setup):
    handler, ec, view, model = base_setup
    handler.tabs_handler.handle = lambda ev: True
    handler.list_handler.handle = lambda ev: (_ for _ in ()).throw(Exception("Should not be called"))
    event = pygame.event.Event(pygame.MOUSEMOTION, {'pos': (0, 0)})
    result = handler.handle(event)
    assert result is True

def test_list_handler_short_circuit(base_setup):
    handler, ec, view, model = base_setup
    handler.tabs_handler.handle = lambda ev: False
    handler.list_handler.handle = lambda ev: True
    event = pygame.event.Event(pygame.MOUSEBUTTONUP, {})
    result = handler.handle(event)
    assert result is True

def test_hover_inside_tab_rects(base_setup):
    handler, ec, view, model = base_setup
    handler.tabs_handler.handle = lambda ev: False
    handler.list_handler.handle = lambda ev: False
    event = pygame.event.Event(pygame.MOUSEMOTION, {'pos': (5, 5)})
    assert handler.handle(event) is True

def test_hover_outside_returns_false(base_setup):
    handler, ec, view, model = base_setup
    handler.tabs_handler.handle = lambda ev: False
    handler.list_handler.handle = lambda ev: False
    event = pygame.event.Event(pygame.MOUSEMOTION, {'pos': (200, 200)})
    assert handler.handle(event) is False
