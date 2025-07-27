import pytest
import pygame

from roguelike_editors.inventory.right_panel.inventory_items_panel.buttons.add_item.add_item_view import AddItemView

@pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    pygame.font.init()
    yield
    pygame.quit()


def test_draw_single_row():
    font = pygame.font.SysFont(None, 24)
    margin = 5
    button_size = (10, 20)
    view = AddItemView(font, button_size, margin)
    overlay = pygame.Surface((200, 200))
    grid_x, grid_y = 0, 0
    slots_count = 3
    mx, my = 5, 65
    res = view.draw(overlay, grid_x, grid_y, mx, my, slots_count)
    assert 'add_item' in res
    rect = res['add_item']
    assert isinstance(rect, pygame.Rect)
    # Expected position: rows = 1, y = grid_y + rows*(50+margin) + margin
    expected_y = grid_y + 1 * (50 + margin) + margin
    assert rect.topleft == (grid_x, expected_y)
    assert rect.size == button_size
    # Collision point inside
    assert rect.collidepoint(mx, my)


def test_draw_multiple_rows_no_hover():
    font = pygame.font.SysFont(None, 24)
    margin = 3
    button_size = (15, 25)
    view = AddItemView(font, button_size, margin)
    overlay = pygame.Surface((200, 300))
    grid_x, grid_y = 10, 10
    slots_count = 6
    # rows = 2
    rows = (slots_count + 5 - 1) // 5
    expected_y = grid_y + rows * (50 + margin) + margin
    mx, my = grid_x + button_size[0] + 1, expected_y + button_size[1] + 1
    res = view.draw(overlay, grid_x, grid_y, mx, my, slots_count)
    rect = res['add_item']
    assert rect.topleft == (grid_x, expected_y)
    # Collision point outside
    assert not rect.collidepoint(mx, my)
