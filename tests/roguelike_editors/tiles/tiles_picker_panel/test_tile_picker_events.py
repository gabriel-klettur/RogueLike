import pytest
import pygame
from types import SimpleNamespace
from pathlib import Path
from roguelike_editors.tiles.tiles_picker_panel.tile_picker_events import TilePickerEventHandler
from roguelike_editors.tiles.tiles_editor_config import PAD, THUMB

class DummyTextInput:
    def __init__(self):
        self.activated = False
        self.args = None
        self.text = ''
        self.active = True
    def activate(self, text, active):
        self.activated = True
        self.args = (text, active)
    def handle_event(self, ev):
        # Simulate user ending input
        self.text = '45'
        self.active = False
        return True

class DummyController:
    def __init__(self):
        self._load_assets_called = False
        self._load_positions_called = False
        self._load_tileset_assets_called = None
        self.base_dir = Path('tiles')
        self.current_dir = self.base_dir
        self.assets = []
        self.view = SimpleNamespace(tileset_text_input=DummyTextInput())
    def _load_assets(self):
        self._load_assets_called = True
    def _load_positions(self):
        self._load_positions_called = True
    def _load_tileset_assets(self, source, grid):
        self._load_tileset_assets_called = (source, grid)

@pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    yield
    pygame.quit()

@pytest.fixture
def handler():
    ctrl = DummyController()
    editor_state = SimpleNamespace(scroll_offset=0, current_choice=None)
    picker_state = SimpleNamespace(
        open=False,
        surface=None,
        pos=(0, 0),
        btn_config_rect=None,
        btn_close_rect=None,
        tileset_checkbox_rect=None,
        tileset_input_rect=None,
        btn_tileset_rect=None,
        tileset_filter=False,
        tileset_input_active=False,
        tileset_source=None,
        config_mode=False,
        config_src_idx=None
    )
    picker_state.tileset_grid_size_text = "32"
    picker_state.tileset_grid_size = int(picker_state.tileset_grid_size_text)
    picker_state.current_choice = None
    handler = TilePickerEventHandler(ctrl, editor_state, picker_state)
    return handler, ctrl, editor_state, picker_state

def test_handle_click_closed(handler):
    h, *_ = handler
    assert not h.handle_click((0, 0), 1, None)

def test_handle_click_outside_surface(handler):
    h, _, _, state = handler
    state.open = True
    state.surface = pygame.Surface((10, 10))
    assert not h.handle_click((20, 20), 1, None)

def test_handle_toolbar_buttons(handler):
    h, _, _, state = handler
    state.btn_config_rect = pygame.Rect(0, 0, 5, 5)
    state.config_mode = False
    assert h._handle_toolbar_buttons(2, 2, None)
    assert state.config_mode is True
    # Toggle back
    assert h._handle_toolbar_buttons(2, 2, None)
    assert state.config_mode is False

def test_handle_tileset_filter_click(handler):
    h, ctrl, _, state = handler
    state.tileset_checkbox_rect = pygame.Rect(0, 0, 5, 5)
    # First click: enable filter
    assert h._handle_tileset_filter_click(2, 2)
    assert state.tileset_filter is True
    assert not ctrl._load_assets_called
    # Second click: disable filter triggers reload
    assert h._handle_tileset_filter_click(2, 2)
    assert state.tileset_filter is False
    assert ctrl._load_assets_called

def test_handle_tileset_input_click(handler):
    h, _, _, state = handler
    h.controller.view.tileset_text_input = DummyTextInput()
    state.tileset_filter = True
    state.tileset_input_rect = pygame.Rect(0, 0, 5, 5)
    assert h._handle_tileset_input_click(2, 2)
    assert state.tileset_input_active is True
    assert h.controller.view.tileset_text_input.activated

def test_handle_tileset_create_click_no_source(handler):
    h, _, _, state = handler
    state.tileset_filter = True
    state.btn_tileset_rect = pygame.Rect(0, 0, 5, 5)
    state.tileset_source = None
    assert not h._handle_tileset_create_click(2, 2, None)

def test_handle_tileset_create_click_with_source(handler):
    h, ctrl, _, state = handler
    state.tileset_filter = True
    state.btn_tileset_rect = pygame.Rect(0, 0, 5, 5)
    state.tileset_source = 'tiles/subdir/image.png'
    # Call create
    assert h._handle_tileset_create_click(2, 2, None)
    # Tileset assets loaded
    assert ctrl._load_tileset_assets_called == ('tiles/subdir/image.png', state.tileset_grid_size)
    # Filter reset
    assert not state.tileset_filter
    assert not state.tileset_input_active
    assert state.tileset_source is None
    # New directory set
    expected = ctrl.base_dir / Path('subdir') / 'image_slices'
    assert ctrl.current_dir == expected
    assert ctrl._load_assets_called
    assert ctrl._load_positions_called

def test_handle_drag_start(handler):
    h, _, _, state = handler
    state.dragging = False
    assert h._handle_drag_start(3, 1, 1)
    assert state.dragging is True
    assert state.drag_offset == (1, 1)

@pytest.mark.parametrize('filter_on', [False, True])
def test_handle_grid_click_file_selection(handler, filter_on):
    h, _, editor_state, state = handler
    # Prepare assets
    h.controller.assets = [('', None, False, None), ('file', None, False, (8, 8))]
    state.open = True
    state.surface = pygame.Surface((100, 100))
    state.tileset_filter = filter_on
    # lx, ly to select idx=1
    lx = PAD + (THUMB + PAD) * 1 + 1
    ly = PAD + 1
    result = h._handle_grid_click(lx, ly, None)
    assert result is True
    if filter_on:
        assert state.tileset_source == 'file'
    else:
        assert editor_state.current_choice == 'file'
        assert state.current_choice == 'file'

def test_handle_event_actions(handler):
    h, ctrl, _, state = handler
    # Test dragging motion
    state.dragging = True
    ctrl.drag = lambda pos: setattr(ctrl, 'drag_called', pos)
    ev = pygame.event.Event(pygame.MOUSEMOTION, pos=(5, 6))
    assert h.handle_event(ev) is True
    assert getattr(ctrl, 'drag_called') == (5, 6)
    # Test stop drag
    state.dragging = True
    ctrl.stop_drag = lambda: setattr(ctrl, 'stopped', True)
    ev = pygame.event.Event(pygame.MOUSEBUTTONUP, button=3)
    assert h.handle_event(ev) is True
    assert getattr(ctrl, 'stopped')
    # Test scroll wheel
    ctrl.scroll = lambda y: setattr(ctrl, 'scrolled', y)
    ev = pygame.event.Event(pygame.MOUSEWHEEL, y=4)
    assert h.handle_event(ev) is True
    assert getattr(ctrl, 'scrolled') == 4
    # Test text input event
    state.tileset_input_active = True
    txt = ctrl.view.tileset_text_input
    ev = pygame.event.Event(pygame.KEYDOWN, key=pygame.K_a)
    assert h.handle_event(ev) is True
    assert state.tileset_grid_size_text == txt.text
    assert state.tileset_grid_size == int(txt.text)
    assert not state.tileset_input_active
