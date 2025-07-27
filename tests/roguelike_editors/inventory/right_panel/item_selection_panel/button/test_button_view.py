import pytest
import pygame
from roguelike_editors.inventory.right_panel.item_selection_panel.button.button_view import ButtonView

@pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    pygame.font.init()
    yield
    pygame.quit()

def test_draw_returns_rect_key():
    font = pygame.font.SysFont(None, 24)
    view = ButtonView(font)
    surface = pygame.Surface((50, 50))
    rect = pygame.Rect(5, 5, 20, 20)
    result = view.draw(surface, rect)
    assert isinstance(result, dict)
    assert 'add_button_rect' in result
    assert result['add_button_rect'] == rect
