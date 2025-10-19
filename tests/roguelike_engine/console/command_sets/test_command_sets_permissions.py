import pytest


def test_inventory_commands_when_game_absent(registry_without_game):
    # Cuando no hay game, los comandos de inventory devuelven mensaje de NA
    out, err = registry_without_game.execute("add inventory potion_small 1")
    assert err is None
    assert "Inventario no disponible" in out

    out, err = registry_without_game.execute("list inventory")
    assert err is None
    assert "Inventario no disponible" in out
