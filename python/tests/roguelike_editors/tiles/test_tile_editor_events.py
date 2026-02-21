import pytest
import pygame
from types import SimpleNamespace
from roguelike_editors.tiles.tile_editor_state import TileEditorState
from roguelike_editors.tiles.tile_editor_events import TileEditorEventHandler

class DummyEventHandler:
    def __init__(self, *args, **kwargs): pass
    def handle_event(self, *args, **kwargs): return False
    def handle_click(self, *args, **kwargs): return False

@pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    yield
    pygame.quit()

@pytest.fixture
def handler(monkeypatch):
    # Stub panel event handlers
    names = [
        'TilePickerEventHandler','TileToolbarEventHandler','TilesViewPanelEventHandler',
        'TilesTitleEventHandler','TilesCollisionPanelEventHandler','LayersPanelEventHandler',
        'SizePanelEventHandler'
    ]
    for name in names:
        monkeypatch.setattr(
            'roguelike_editors.tiles.tile_editor_events.' + name,
            DummyEventHandler
        )
    state = SimpleNamespace(running=True)
    editor_state = TileEditorState()
    # Stub nested controllers and states existence
    controller = SimpleNamespace(
        picker=SimpleNamespace(picker_state=editor_state.picker_state, is_over=lambda pos: False),
        toolbar=SimpleNamespace(),
        view_panel_controller=SimpleNamespace(),
        title_controller=SimpleNamespace(),
        collision_panel_controller=SimpleNamespace(),
        layers_panel_controller=SimpleNamespace(),
        size_panel_controller=SimpleNamespace()
    )
    handler = TileEditorEventHandler(state, editor_state, controller)
    return handler, state, editor_state, controller


def test_on_quit(handler):
    h, state, es, ctrl = handler
    ev = SimpleNamespace(type=pygame.QUIT)
    h.handle([ev], None, None)
    assert state.running is False


def test_on_keydown_escape(handler):
    h, state, es, ctrl = handler
    # set initial flags
    es.active = True
    es.selected_tile = 'tile'
    es.picker_state.open = True
    es.brush_dragging = True
    ev = SimpleNamespace(type=pygame.KEYDOWN, key=pygame.K_ESCAPE)
    h.handle([ev], None, None)
    assert es.active is False
    assert es.selected_tile is None
    assert es.picker_state.open is False
    assert es.brush_dragging is False


def test_on_keydown_f8(handler):
    h, state, es, ctrl = handler
    # initial inactive
    es.active = False
    ev = SimpleNamespace(type=pygame.KEYDOWN, key=pygame.K_F8)
    h.handle([ev], None, None)
    assert es.active is True
    assert es.toolbar_state.view_active is True
    assert es.size_panel_state.visible is True


def test_mouse_down_brush_start_and_flush(handler):
    h, state, es, ctrl = handler
    # stub start_brush and flush_brush
    called = {}
    ctrl.start_brush = lambda: called.setdefault('start', True)
    ctrl.apply_brush = lambda pos, camera, m: called.setdefault('apply', True)
    ctrl.flush_brush = lambda m, c: called.setdefault('flush', True)
    # Test MOUSEBUTTONDOWN brush start
    es.current_tool = 'brush'
    ev_down = SimpleNamespace(type=pygame.MOUSEBUTTONDOWN, button=1, pos=(0,0))
    h.handle([ev_down], None, None)
    assert called.get('start')
    # Test MOUSEBUTTONUP flush brush
    ev_up = SimpleNamespace(type=pygame.MOUSEBUTTONUP, button=1)
    h.handle([ev_up], None, None)
    assert called.get('flush')
