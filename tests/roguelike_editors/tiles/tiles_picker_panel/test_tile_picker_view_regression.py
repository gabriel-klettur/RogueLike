import pytest
import pygame
from types import SimpleNamespace
from roguelike_editors.tiles.tiles_picker_panel.tile_picker_view import TilePickerView
from roguelike_editors.tiles.tiles_editor_config import THUMB, PAD, COLS, BTN_H

@ pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    pygame.display.set_mode((1, 1))
    yield
    pygame.quit()

@ pytest.fixture
def setup_view(monkeypatch):
    # Dummy controller and state configured for config_mode
    controller = SimpleNamespace()
    picker_state = SimpleNamespace(
        scroll_offset=0,
        pos=(0, 0),
        open=False,
        config_mode=True,
        config_src_idx=None,
        current_choice=None
    )
    assets = []
    # Monkeypatch ScrollableGrid to minimal stub
    class DummyGrid:
        def __init__(self, thumb, pad, count, scroll, cols):
            pass
        def compute(self):
            return (1, 1, THUMB, THUMB)
        def draw_items(self, surface, assets, panel_pos, draw_fn):
            raise AssertionError("Fallback draw_items should not be used in config_mode")
    monkeypatch.setattr(
        'roguelike_editors.tiles.tiles_picker_panel.tile_picker_view.ScrollableGrid',
        DummyGrid
    )
    view = TilePickerView(controller, picker_state, assets)
    view.panel = SimpleNamespace(surface=pygame.Surface((THUMB, THUMB)), pos=(0, 0))
    return view


def test_selection_overlay_attribute_exists(setup_view):
    """selection_overlay attribute must be created in __init__"""
    view = setup_view
    assert hasattr(view, 'selection_overlay'), "TilePickerView missing selection_overlay attribute"
    assert isinstance(view.selection_overlay, pygame.Surface)


def test_draw_assets_grid_config_mode_selection_overlay_no_error(setup_view, monkeypatch):
    """_draw_assets_grid should not raise when drawing selection_overlay in config_mode"""
    view = setup_view
    # Prepare assets and state
    surf_tile = pygame.Surface((THUMB, THUMB))
    view.assets = [("val", surf_tile, False, (3, 4))]
    view.picker_state.current_choice = "val"
    view.picker_state.config_src_idx = None
    # Position mouse outside cell to skip hover
    monkeypatch.setattr(
        'roguelike_editors.tiles.tiles_picker_panel.tile_picker_view.pygame.mouse.get_pos',
        lambda: (100, 100)
    )
    # Dummy grid matches compute params
    class DummyGrid2:
        def __init__(self, *args, **kwargs): pass
        def compute(self): return (1, 1, THUMB, THUMB)
        def draw_items(self, *args, **kwargs):
            raise AssertionError("Fallback draw_items should not be used in config_mode")
    # Should not raise exception
    result = view._draw_assets_grid(DummyGrid2())
    assert result == (None, None)
