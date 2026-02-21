import pytest
import pygame
from types import SimpleNamespace
from roguelike_editors.tiles.tile_editor_view import TileEditorView
from roguelike_editors.tiles.tile_editor_state import TileEditorState

@pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    yield
    pygame.quit()

@pytest.fixture
def setup_view():
    called = {}
    controller = SimpleNamespace(
        title_controller=SimpleNamespace(render=lambda s: called.setdefault('title', True)),
        toolbar=SimpleNamespace(view=SimpleNamespace(render=lambda s: called.setdefault('toolbar', True))),
        size_panel_controller=SimpleNamespace(render=lambda s: called.setdefault('size', True)),
        picker=SimpleNamespace(view=SimpleNamespace(render=lambda s: called.setdefault('picker', True))),
        view_panel_controller=SimpleNamespace(render=lambda s, c, m: called.setdefault('view_panel', True)),
        layers_panel_controller=SimpleNamespace(render=lambda s: called.setdefault('layers', True)),
        collision_panel_controller=SimpleNamespace(render=lambda s: called.setdefault('collision', True)),
        outline_view=SimpleNamespace(render=lambda s, c, m: called.setdefault('outline', True))
    )
    state = TileEditorState()
    controller.editor = state
    view = TileEditorView(controller, state)
    return view, controller, state, called


def test_render_inactive(setup_view):
    view, ctrl, state, called = setup_view
    screen = pygame.Surface((10, 10))
    camera = None
    game_map = None
    state.active = False
    view.render(screen, camera, game_map)
    assert 'outline' not in called


def test_render_active_calls_outline(setup_view):
    view, ctrl, state, called = setup_view
    screen = pygame.Surface((50, 50))
    camera = SimpleNamespace(offset_x=0, offset_y=0, apply=lambda pos: pos)
    game_map = None
    state.active = True
    # disable other panels
    state.picker_state.open = False
    state.toolbar_state.view_active = False
    state.toolbar_state.layers_view_open = False
    state.toolbar_state.collision_picker_open = False
    state.size_panel_state.visible = False
    view.render(screen, camera, game_map)
    assert called.get('outline') is True
