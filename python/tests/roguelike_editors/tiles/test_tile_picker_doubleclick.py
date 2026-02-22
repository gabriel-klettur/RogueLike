import types
import pygame
import pytest
from pathlib import Path

from roguelike_editors.tiles.tiles_picker_panel.tile_picker_events import TilePickerEventHandler
from roguelike_editors.tiles.tiles_editor_config import THUMB, PAD, COLS

class DummyPickerState:
    def __init__(self):
        self.open = True
        self.surface = pygame.Surface((400, 300))
        self.pos = (100, 70)
        self.config_mode = False
        self.tileset_checkbox_rect = None
        self.tileset_filter = False
        self.tileset_input_active = False
        self.tileset_source = None
        self.tileset_input_rect = None
        self.btn_tileset_rect = None
        self.btn_config_rect = None
        self.btn_close_rect = None
        # hover/selection not needed here

class DummyEditorState:
    def __init__(self):
        self.scroll_offset = 0
        self.current_choice = None

class DummyView:
    def __init__(self):
        self.tileset_text_input = types.SimpleNamespace(activate=lambda *a, **k: None)

class DummyPickerController:
    def __init__(self):
        self.base_dir = Path('.')
        self.current_dir = self.base_dir / 'assets' / 'tiles'
        # create simple assets: first row includes directory named 'dirA'
        # asset format: (value, surface, is_dir, size)
        self.assets = []
        # simulate 3*COLS columns grid (we don't import config here; choose a safe layout)
        # We'll ensure our click computes index 1 to be dirA
        for i in range(10):
            if i == 1:
                self.assets.append(('dirA', None, True, None))
            else:
                self.assets.append(('', None, False, None))
        self.view = DummyView()
        self._loads = 0
    def _load_assets(self):
        self._loads += 1
    def _load_positions(self):
        pass

@pytest.fixture
def picker_handler():
    controller = DummyPickerController()
    editor_state = DummyEditorState()
    picker_state = DummyPickerState()
    return controller, editor_state, picker_state, TilePickerEventHandler(controller, editor_state, picker_state)


def _local_click(handler, picker_state, x, y, button=1):
    # Convert local picker coords to screen coords
    sx = picker_state.pos[0] + x
    sy = picker_state.pos[1] + y
    handler.handle_click((sx, sy), button, map=None)


def test_single_click_does_not_open_dir(picker_handler, monkeypatch):
    controller, editor_state, picker_state, handler = picker_handler
    # Click on idx=1: row=0, col=1
    lx = PAD + (THUMB + PAD) * 1 + THUMB // 2
    ly = PAD + THUMB // 2
    now = 1000
    monkeypatch.setattr(pygame.time, 'get_ticks', lambda: now)
    _local_click(handler, picker_state, x=lx, y=ly, button=1)
    assert controller._loads == 0, "Single click must not open folder or load assets"


def test_double_click_within_window_opens_dir(picker_handler, monkeypatch):
    controller, editor_state, picker_state, handler = picker_handler
    t0 = 2000
    # first click
    monkeypatch.setattr(pygame.time, 'get_ticks', lambda: t0)
    lx = PAD + (THUMB + PAD) * 1 + THUMB // 2
    ly = PAD + THUMB // 2
    _local_click(handler, picker_state, x=lx, y=ly, button=1)
    # second click within 900ms
    monkeypatch.setattr(pygame.time, 'get_ticks', lambda: t0 + 500)
    _local_click(handler, picker_state, x=lx, y=ly, button=1)
    assert controller._loads >= 1, "Double click should trigger asset load/open folder"


def test_non_left_click_on_dir_is_ignored(picker_handler, monkeypatch):
    controller, editor_state, picker_state, handler = picker_handler
    now = 3000
    monkeypatch.setattr(pygame.time, 'get_ticks', lambda: now)
    lx = PAD + (THUMB + PAD) * 1 + THUMB // 2
    ly = PAD + THUMB // 2
    _local_click(handler, picker_state, x=lx, y=ly, button=3)
    assert controller._loads == 0, "Right-click must be ignored on directories"
