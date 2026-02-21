import pytest
from roguelike_engine.console.contexts.inventory import InventoryContext


def test_context_small_integration(fake_game_with_inventory):
    ctx = InventoryContext(fake_game_with_inventory)
    assert ctx.list() == "Inventario vacío"

    ctx.add("weapons", "sword", 2)
    ctx.edit("weapons", "sword", "quantity", "3")
    out = ctx.list()
    assert "weapons_sword: 3" in out

    ctx.remove("weapons", "sword", 3)
    assert ctx.list() == "Inventario vacío"
