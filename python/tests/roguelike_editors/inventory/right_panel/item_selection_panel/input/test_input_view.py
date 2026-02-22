import pytest
import pygame
from roguelike_editors.inventory.right_panel.item_selection_panel.input.input_view import InputView

@pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    pygame.font.init()
    yield
    pygame.quit()


def test_draw_syncs_text_and_returns_key():
    font = pygame.font.SysFont(None, 24)
    view = InputView(font)
    # initial state: inactive with custom text
    view.text_input.active = False
    view.text_input.text = 'old'
    surface = pygame.Surface((100, 100))
    rect = pygame.Rect(10, 10, 50, 20)
    result = view.draw(surface, 7, rect)
    assert isinstance(result, dict)
    assert 'input_rect' in result
    assert result['input_rect'] == rect
    assert view.text_input.text == '7'


def test_draw_keeps_text_when_active():
    font = pygame.font.SysFont(None, 24)
    view = InputView(font)
    view.text_input.active = True
    view.text_input.text = 'keep'
    surface = pygame.Surface((100, 100))
    rect = pygame.Rect(0, 0, 30, 10)
    result = view.draw(surface, 9, rect)
    # text should remain unchanged
    assert view.text_input.text == 'keep'
    assert result == {'input_rect': rect}
