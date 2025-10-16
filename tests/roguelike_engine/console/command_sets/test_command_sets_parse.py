import pytest


def test_parser_quotes_and_split(registry_with_game):
    # Cita con espacios debe preservarse como un único argumento
    out, err = registry_with_game.execute('echo "hola mundo"')
    assert err is None
    assert out == "hola mundo"

    # shlex fallback: comillas sin cerrar => split simple
    out, err = registry_with_game.execute('echo "hola mundo')
    assert err is None
    assert out == '"hola mundo'  # split clásico produce dos tokens y echo los une


def test_autocomplete_names_and_args(registry_with_game):
    # Al inicio sugiere comandos/aliases
    names = registry_with_game.autocomplete("")
    for expected in ("help", "echo", "quit", "godmode", "add", "remove", "edit", "list", "listitems"):
        assert expected in names

    # Prefijo de nombre
    assert set(registry_with_game.autocomplete("e")) >= {"echo", "edit"}

    # Completer de contexto para inventory
    assert registry_with_game.autocomplete("add ") == ["inventory"]
