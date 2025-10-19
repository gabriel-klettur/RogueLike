import pytest
from roguelike_engine.console.contexts.inventory import InventoryContext


def test_inventory_context_user_messages(fake_game_with_inventory):
    ctx = InventoryContext(fake_game_with_inventory)

    # Mensaje de inventario vacío
    assert ctx.list() == "Inventario vacío"

    # Mensajes de resultado informativos
    out = ctx.add_direct("weapons_sword", 1)
    assert "Añadidos 1x weapons_sword" in out

    out = ctx.remove_direct("weapons_sword", 1)
    assert "Eliminados 1x weapons_sword" in out
