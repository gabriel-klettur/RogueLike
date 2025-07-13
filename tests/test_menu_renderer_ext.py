import pytest
import pygame
from roguelike_ui.widgets.menu_renderer import MenuRenderer

@pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    yield
    pygame.quit()


def test_surface_properties():
    renderer = MenuRenderer(font_size=30)
    # Surface dimensions and alpha
    assert renderer.surface.get_size() == (400, 250)
    assert renderer.surface.get_alpha() == 240
