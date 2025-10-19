import pytest


def test_help_general_and_specific(registry_with_game):
    out, err = registry_with_game.execute("help")
    assert err is None
    # Debe listar categorías, al menos core e inventory
    assert "[core]" in out
    assert "[inventory]" in out

    out, err = registry_with_game.execute("help echo")
    assert err is None
    assert "Uso:" in out
    assert "echo" in out

    out, err = registry_with_game.execute("help unknown")
    assert err is None
    assert "Comando desconocido" in out
