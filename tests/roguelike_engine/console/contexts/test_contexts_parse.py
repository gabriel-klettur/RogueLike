import pytest
from roguelike_engine.console.contexts.inventory import InventoryContext


def test_inventory_context_direct_vs_category(fake_game_with_inventory):
    ctx = InventoryContext(fake_game_with_inventory)

    out = ctx.add_direct("weapons_sword", 1)
    assert "Añadidos 1x weapons_sword" in out

    out = ctx.add("weapons", "sword", 1)
    assert "Añadidos 1x weapons_sword" in out

    out = ctx.list()
    # Debe haber 2 en total
    assert "weapons_sword: 2" in out
