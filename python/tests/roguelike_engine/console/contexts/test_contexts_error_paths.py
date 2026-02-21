import pytest
from roguelike_engine.console.contexts.inventory import InventoryContext


def test_inventory_context_errors(fake_game_with_inventory):
    ctx = InventoryContext(fake_game_with_inventory)

    # item desconocido
    out = ctx.add_direct("unknown", 1)
    assert "Item desconocido" in out

    # no hay suficiente al quitar
    out = ctx.remove_direct("weapons_sword", 1)
    assert "No hay suficiente" in out

    # editar errores
    # primero añade 1
    ctx.add_direct("weapons_sword", 1)
    out = ctx.edit_direct("weapons_sword", "quantity", "abc")
    assert "Valor inválido" in out

    out = ctx.edit_direct("weapons_sword", "foo", "1")
    assert "Propiedad desconocida" in out
