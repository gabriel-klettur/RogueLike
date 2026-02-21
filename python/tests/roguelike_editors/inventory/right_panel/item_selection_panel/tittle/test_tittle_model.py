import pytest
from roguelike_editors.inventory.right_panel.item_selection_panel.tittle.tittle_model import TittleModel


def test_default_show_panel():
    model = TittleModel()
    assert model.show_panel is False


def test_toggle_show_panel():
    model = TittleModel()
    model.show_panel = True
    assert model.show_panel is True
    model.show_panel = False
    assert model.show_panel is False
