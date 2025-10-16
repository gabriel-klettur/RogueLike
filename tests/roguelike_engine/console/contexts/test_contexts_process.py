import pytest
from types import SimpleNamespace

from roguelike_engine.console.contexts.inventory import InventoryContext


def test_inventory_context_basic_flow(fake_game_with_inventory):
    ctx = InventoryContext(fake_game_with_inventory)

    # add vía category+key
    out = ctx.add("weapons", "sword", 2)
    assert "Añadidos 2x weapons_sword" in out

    # list
    out = ctx.list()
    assert "weapons_sword: 2" in out

    # edit quantity
    out = ctx.edit("weapons", "sword", "quantity", "5")
    assert "weapons_sword cantidad ajustada a 5" in out

    # remove
    out = ctx.remove("weapons", "sword", 3)
    assert "Eliminados 3x weapons_sword" in out
