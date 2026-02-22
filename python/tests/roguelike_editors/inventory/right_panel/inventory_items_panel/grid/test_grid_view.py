import pytest
import pygame
from types import SimpleNamespace

from roguelike_editors.inventory.right_panel.inventory_items_panel.grid.grid_view import GridView

@pytest.fixture(autouse=True)
def init_pygame():
    pygame.init()
    pygame.font.init()
    yield
    pygame.quit()

def test_get_slot_index_detects_correct_index():
     slot_size = 20
     margin = 5
     view = GridView(None, slot_size, margin, None, None)
     # index 7: col=2,row=1
     pos_x = 2 * (slot_size + margin) + slot_size // 2
     pos_y = 1 * (slot_size + margin) + slot_size // 2
     idx = view.get_slot_index((pos_x, pos_y), 0, 0, 10)
     assert idx == 7

def test_get_slot_index_outside_returns_none():
     view = GridView(None, 10, 2, None, None)
     idx = view.get_slot_index((100, 100), 0, 0, 4)
     assert idx is None

def test_draw_slots_no_error_and_hover_highlight():
     font = pygame.font.SysFont(None, 24)
     slot_size = 15
     margin = 3
     # get_item_image returns Surface
     def get_item_image(item):
         return pygame.Surface((10, 10))
     errors = []
     logger = SimpleNamespace(error=lambda msg: errors.append(msg))
     view = GridView(font, slot_size, margin, get_item_image, logger)
     overlay = pygame.Surface((200, 200), pygame.SRCALPHA)
     slots = [None, {'item': 'X', 'quantity': 2}]
     # without delete mode
     view.draw_slots(overlay, slots, 0, 0, -1, -1, delete_mode_active=False)
     assert errors == []
     # with faulty get_item_image and delete mode hover
     def faulty_get_image(item):
         raise Exception("fail")
     view_err = GridView(font, slot_size, margin, faulty_get_image, logger)
     overlay2 = pygame.Surface((200, 200), pygame.SRCALPHA)
     errs_before = len(errors)
     # position over slot 0
     mx = slot_size // 2
     my = slot_size // 2
     view_err.draw_slots(overlay2, slots, 0, 0, mx, my, delete_mode_active=True)
     assert len(errors) > errs_before
