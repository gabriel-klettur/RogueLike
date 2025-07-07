import pygame
from roguelike_editors.inventory.view.editor_view import InventoryEditorView
from roguelike_editors.inventory.model.editor_model import InventoryEditorModel
from types import SimpleNamespace
import pytest


def test_get_slot_at_pos_basic(use_pygame, screen):
    font = pygame.font.SysFont(None, 16)
    view = InventoryEditorView(assets={}, font=font)
    # Position over first slot
    origin_x, origin_y = view.grid_origin
    x = origin_x + 1
    y = origin_y + 30 + 1
    idx = view.get_slot_at_pos((x, y), count=5)
    assert idx == 0
    # Position outside any slot
    idx2 = view.get_slot_at_pos((0,0), count=5)
    assert idx2 is None

@pytest.fixture
def use_pygame():
    pygame.init()
    yield
    pygame.quit()
