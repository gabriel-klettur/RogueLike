import pytest
import pygame

from roguelike_editors.inventory.right_panel.inventory_items_panel.buttons.delete.delete_view import DeleteView

@pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    pygame.font.init()
    yield
    pygame.quit()


def test_draw_button_positions_and_hover():
    font = pygame.font.SysFont(None, 24)
    margin = 5
    button_size = (20, 30)
    view = DeleteView(font, button_size, margin)
    overlay = pygame.Surface((200, 200))
    grid_x, grid_y = 10, 10
    slots_count = 7
    # rows = (slots+5-1)//5 = 2
    rows = (slots_count + 5 - 1) // 5
    manage_y = grid_y + rows * (50 + margin) + margin
    mx, my = grid_x + button_size[0] + margin + 1, manage_y + 1
    res = view.draw_button(overlay, grid_x, grid_y, mx, my, slots_count, delete_mode_active=False)
    assert 'delete_item' in res
    rect = res['delete_item']
    # position and size
    assert rect.topleft == (grid_x + button_size[0] + margin, manage_y)
    assert rect.size == button_size
    # hover detection
    assert rect.collidepoint(mx, my)


def test_draw_input_sets_delete_qty_input_rect():
    font = pygame.font.SysFont(None, 24)
    margin = 5
    button_size = (20, 30)
    view = DeleteView(font, button_size, margin)
    overlay = pygame.Surface((300, 300))
    grid_x, grid_y = 5, 5
    slots_count = 3
    # draw button first to get delete_item_rect
    btn_res = view.draw_button(overlay, grid_x, grid_y, 0, 0, slots_count, delete_mode_active=False)
    delete_item_rect = btn_res['delete_item']
    # define add_item_rect arbitrarily
    add_item_rect = pygame.Rect(grid_x, btn_res['delete_item'].y, *button_size)
    mx, my = 0, 0
    view.draw_input(overlay, grid_x, grid_y, mx, my, slots_count, add_item_rect, delete_item_rect)
    # After drawing input, delete_qty_input_rect should be set
    assert isinstance(view.delete_qty_input_rect, pygame.Rect)
    # y coordinate aligns with qty_y - 2
    rows = (slots_count + 5 - 1) // 5
    manage_y = grid_y + rows * (50 + margin) + margin
    qty_y = manage_y + button_size[1] + margin
    assert view.delete_qty_input_rect.y == qty_y - 2
