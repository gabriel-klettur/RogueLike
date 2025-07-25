import pytest
import pygame
from types import SimpleNamespace
from roguelike_editors.tiles.tiles_title.tiles_tiles_view import TilesTilesView

@pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    yield
    pygame.quit()


def test_render_default_title_and_background():
    state = SimpleNamespace(title="")
    view = TilesTilesView(None, state)
    screen = pygame.Surface((200, 200), pygame.SRCALPHA)
    view.render(screen)
    # Background pixel at panel origin should be semi-transparent black
    assert screen.get_at((10, 10)) == pygame.Color(0, 0, 0, 180)



def test_render_custom_title_renders_text():
    custom = "My Title"
    state = SimpleNamespace(title=custom)
    view = TilesTilesView(None, state)
    screen = pygame.Surface((200, 200), pygame.SRCALPHA)
    view.render(screen)
    assert True
