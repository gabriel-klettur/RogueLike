import pygame
import pytest
from roguelike_ui.widgets.menu_renderer import MenuRenderer

@pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    yield
    pygame.quit()


def test_background_fill_and_surface_init():
    renderer = MenuRenderer(font_size=25)
    # Surface initialized correctly
    assert renderer.surface.get_size() == (400, 250)
    assert renderer.surface.get_alpha() == 240
    # Background fill on draw
    screen = pygame.Surface((800, 600))
    options = ["A"]
    renderer.draw(screen, 0, options)
    # Top-left pixel should be background color
    assert renderer.surface.get_at((0, 0))[:3] == renderer.bg_color


def test_render_colors_and_positions():
    screen = pygame.Surface((800, 600))
    renderer = MenuRenderer(font_size=20)
    options = ["Opt1", "Opt2"]
    selected = 1
    # Capture render colors
    colors = []
    class DummyFont:
        def render(self, text, aa, color):
            colors.append(color)
            return pygame.Surface((10, 10))
    renderer.font = DummyFont()
    # Draw and get rect
    rect = renderer.draw(screen, selected, options)
    # Verify colors
    assert colors[0] == renderer.default_color
    assert colors[1] == renderer.selected_color
    # Verify blit positions captured
    assert renderer.last_blits == [(50, 40), (50, 40 + 1 * 50)]
    # Verify returned rect is centered
    expected_x = (800 - renderer.surface.get_size()[0]) // 2
    expected_y = (600 - renderer.surface.get_size()[1]) // 2
    assert rect.topleft == (expected_x, expected_y)
