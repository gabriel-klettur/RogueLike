import pytest


def test_unknown_command_returns_message(registry_with_game):
    out, err = registry_with_game.execute("foobarcmd")
    assert err is None
    assert out.startswith("Unknown command:")


def test_invalid_items_and_quantities(registry_with_game):
    out, err = registry_with_game.execute("add inventory category unknownitem 1")
    assert err is None
    assert "Item desconocido" in out

    out, err = registry_with_game.execute("remove inventory weapons sword 1")
    assert err is None
    assert "No hay suficiente" in out


def test_inventory_full(registry_with_game):
    # Capacidad=1 (fixture). Llenar con un ítem y luego intentar añadir otro distinto.
    registry_with_game.execute("add inventory weapons sword 1")
    out, err = registry_with_game.execute("add inventory armor leather 1")
    assert err is None
    assert "inventario lleno" in out.lower()


def test_edit_error_messages(registry_with_game):
    out, _ = registry_with_game.execute("edit inventory weapons sword foo 10")
    assert "Propiedad desconocida" in out

    out, _ = registry_with_game.execute("edit inventory weapons sword quantity abc")
    assert "Valor inválido" in out

    out, _ = registry_with_game.execute("edit inventory weapons sword quantity 1")
    assert "Item weapons_sword no encontrado" in out  # aún no existe
