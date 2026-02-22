import pytest
import pygame
from types import SimpleNamespace
from roguelike_editors.tiles.tile_outline_view import TileOutlineView
from roguelike_editors.tiles.tile_editor_state import TileEditorState
from roguelike_editors.tiles.tiles_editor_config import OUTLINE_HOVER, OUTLINE_SEL

@pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    yield
    pygame.quit()

@pytest.fixture
def setup_outline(monkeypatch):
    # Dummy tile
    class Tile:
        def __init__(self, x, y):
            self.x = x
            self.y = y
    tile = Tile(0, 0)
    # Dummy controller and editor_state
    editor_state = TileEditorState()
    controller = SimpleNamespace(
        _tile_under_mouse=lambda pos, cam, m: tile,
        editor=editor_state
    )
    view = TileOutlineView(controller, editor_state)
    # Dummy camera
    camera = SimpleNamespace(
        apply=lambda p: p
    )
    screen = pygame.Surface((100, 100))
    return view, controller, editor_state, camera, screen, tile


def test_hover_draw(monkeypatch, setup_outline):
    view, controller, editor_state, camera, screen, tile = setup_outline
    editor_state.current_tool = 'brush'
    calls = []
    monkeypatch.setattr(pygame.draw, 'rect', lambda surf, color, rect, width=0, **kwargs: calls.append((color, rect)))
    view.render(screen, camera, None)
    # First draw is hover outline
    assert any(call[0] == OUTLINE_HOVER for call in calls)


def test_selected_draw(monkeypatch, setup_outline):
    view, controller, editor_state, camera, screen, tile = setup_outline
    # Set selected tile
    editor_state.selected_tile = tile
    # Prevent hover drawing by setting _tile_under_mouse to None
    controller._tile_under_mouse = lambda pos, cam, m: None
    calls = []
    monkeypatch.setattr(pygame.draw, 'rect', lambda surf, color, rect, width=0, **kwargs: calls.append((color, rect)))
    view.render(screen, camera, None)
    # At least one draw uses selection color
    assert any(call[0] == OUTLINE_SEL for call in calls)

def test_no_hover_without_brush(monkeypatch, setup_outline):
    view, controller, editor_state, camera, screen, tile = setup_outline
    assert editor_state.current_tool != 'brush'
    calls = []
    monkeypatch.setattr(pygame.draw, 'rect', lambda surf, color, rect, width=0, **kwargs: calls.append((color, rect)))
    view.render(screen, camera, None)
    assert not any(call[0] == OUTLINE_HOVER for call in calls)
