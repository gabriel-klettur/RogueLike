import pytest
import pygame
from types import SimpleNamespace
from roguelike_editors.tiles.layers_panel.layers_panel_view import LayersPanelView
from roguelike_editors.tiles.layers_panel.layers_panel_states import LayersPanelState
from roguelike_engine.map.model.layer import Layer
from roguelike_editors.tiles.tiles_editor_config import PAD

@ pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    yield
    pygame.quit()


def make_dummy_controller(icon_present=True):
    if icon_present:
        icon_rect = pygame.Rect(10, 20, 30, 40)
        toolbar = SimpleNamespace(icon_rects={'view_layers': icon_rect}, padding=5, x=0, y=0, size=50)
    else:
        toolbar = SimpleNamespace(icon_rects={}, padding=5, x=100, y=200, size=20)
    editor_controller = SimpleNamespace(toolbar=toolbar)
    editor_state = SimpleNamespace(toolbar_state=SimpleNamespace(show_buildings=False))
    controller = SimpleNamespace(editor_controller=editor_controller, editor_state=editor_state)
    return controller


def test_ensure_panel_position_with_icon():
    state = LayersPanelState()
    controller = make_dummy_controller(icon_present=True)
    view = LayersPanelView(controller, state)
    view._ensure_panel_position()
    icon_rect = controller.editor_controller.toolbar.icon_rects['view_layers']
    expected = (icon_rect.right + controller.editor_controller.toolbar.padding, icon_rect.y)
    assert state.pos == expected
    assert view.panel.pos == expected


def test_ensure_panel_position_without_icon():
    state = LayersPanelState()
    controller = make_dummy_controller(icon_present=False)
    view = LayersPanelView(controller, state)
    view._ensure_panel_position()
    tb = controller.editor_controller.toolbar
    expected = (tb.x + tb.size + PAD, tb.y)
    assert state.pos == expected
    assert view.panel.pos == expected


def test_render_populates_option_rects():
    state = LayersPanelState()
    controller = make_dummy_controller(icon_present=True)
    view = LayersPanelView(controller, state)
    screen = pygame.Surface((500, 500))
    view.render(screen)
    expected_keys = set(list(Layer)) | {'buildings'}
    assert set(state.option_rects.keys()) == expected_keys
    for rect in state.option_rects.values():
        assert isinstance(rect, pygame.Rect)
