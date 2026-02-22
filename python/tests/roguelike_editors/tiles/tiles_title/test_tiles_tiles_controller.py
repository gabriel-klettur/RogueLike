import pytest
import pygame
from types import SimpleNamespace
from roguelike_editors.tiles.tiles_title.tiles_tiles_controller import TilesTitleController
from roguelike_editors.tiles.tiles_title.tiles_tiles_view import TilesTilesView

@pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    yield
    pygame.quit()


def test_init_properties():
    editor_state = SimpleNamespace()
    state = SimpleNamespace()
    controller = TilesTitleController(editor_state, state)
    assert controller.editor_state is editor_state
    assert controller.state is state
    assert isinstance(controller.view, TilesTilesView)


def test_render_delegates_to_view():
    editor_state = SimpleNamespace()
    state = SimpleNamespace()
    controller = TilesTitleController(editor_state, state)
    # Replace view with dummy
    dummy_view = SimpleNamespace()
    calls = {}
    def fake_render(screen):
        calls['screen'] = screen
    dummy_view.render = fake_render
    controller.view = dummy_view
    screen = pygame.Surface((20, 10))
    controller.render(screen)
    assert calls.get('screen') is screen
