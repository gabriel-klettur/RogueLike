import pytest
import pygame

from roguelike_editors.inventory.right_panel.inventory_items_panel.buttons.save.save_view import SaveView

@pytest.fixture(autouse=True)

def init_pygame():
    pygame.init()
    pygame.font.init()
    yield
    pygame.quit()


def test_draw_without_delete_mode():
    font = pygame.font.SysFont(None, 24)
    margin = 5
    button_size = (20, 10)
    view = SaveView(font, button_size, margin)
    overlay = pygame.Surface((200, 200))
    grid_x, grid_y = 0, 0
    slots_count = 3
    # rows = 1
    rows = (slots_count + 5 - 1) // 5
    base_y = grid_y + rows * (50 + margin) + margin
    expected_y = base_y + button_size[1] + margin
    mx, my = grid_x + 1, expected_y + 1
    res = view.draw(overlay, grid_x, grid_y, mx, my, slots_count, delete_mode_active=False)
    rect = res['save']
    assert rect.topleft == (grid_x, expected_y)
    assert rect.size == (button_size[0] * 2 + margin, button_size[1])
    assert rect.collidepoint(mx, my)


def test_draw_with_delete_mode():
    font = pygame.font.SysFont(None, 24)
    margin = 5
    button_size = (20, 10)
    view = SaveView(font, button_size, margin)
    overlay = pygame.Surface((200, 200))
    grid_x, grid_y = 0, 0
    slots_count = 3
    rows = (slots_count + 5 - 1) // 5
    base_y = grid_y + rows * (50 + margin) + margin
    initial_y = base_y + button_size[1] + margin
    expected_y = initial_y + button_size[1] + margin
    mx, my = grid_x + 1, expected_y + 1
    res = view.draw(overlay, grid_x, grid_y, mx, my, slots_count, delete_mode_active=True)
    rect = res['save']
    assert rect.topleft == (grid_x, expected_y)
    assert rect.size == (button_size[0] * 2 + margin, button_size[1])
    assert rect.collidepoint(mx, my)
