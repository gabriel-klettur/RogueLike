import pytest
import pygame
from roguelike_editors.inventory.right_panel.item_selection_panel.tittle.tittle_view import TittleView

@pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    pygame.font.init()
    yield
    pygame.quit()


def test_draw_returns_correct_rects():
    font = pygame.font.SysFont(None, 24)
    margin = 5
    view = TittleView(font, margin)
    surface = pygame.Surface((200, 200))
    panel_rect = pygame.Rect(10, 20, 100, 50)
    result = view.draw(surface, panel_rect)
    # Verify keys and panel_rect
    assert isinstance(result, dict)
    assert 'panel_rect' in result and 'header_rect' in result
    assert result['panel_rect'] == panel_rect
    # Verify header_rect dimensions and position
    header_rect = result['header_rect']
    title = "Item List"
    title_surf = font.render(title, True, (255, 255, 255))
    expected_h = title_surf.get_height() + margin
    assert header_rect.width == panel_rect.width
    assert header_rect.height == expected_h
    assert header_rect.x == panel_rect.x
    assert header_rect.y == panel_rect.y - expected_h
