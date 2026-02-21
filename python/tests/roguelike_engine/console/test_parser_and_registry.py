import pytest
from roguelike_engine.console.parser import ConsoleParser
from roguelike_engine.console.console_model import CommandRegistry
from roguelike_engine.console import register_commands


def test_parser_handles_quotes():
    p = ConsoleParser()
    tokens = p.tokenize('echo "hola mundo" test')
    assert tokens == ['echo', 'hola mundo', 'test']


def test_registry_register_and_autocomplete_core():
    reg = CommandRegistry()
    # game=None -> inventory se registra como stubs; core siempre disponible
    register_commands(reg, game=None)

    # Autocompletar nombre de comando
    opts = reg.autocomplete('he')
    assert 'help' in opts
    # Alias de help
    assert '?' in (set(reg.commands.keys()) | set(reg.alias_to_name.keys()))

    # Ejecutar echo
    out, err = reg.execute('echo hola mundo')
    assert err is None
    assert out == 'hola mundo'


def test_help_metadata_output():
    reg = CommandRegistry()
    register_commands(reg, game=None)

    out, err = reg.execute('help echo')
    assert err is None
    assert 'Uso:' in out or 'Imprime el texto' in out
