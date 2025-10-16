import pytest


def test_register_commands_exposes_parser_and_autocomplete(fake_game_with_inventory):
    from roguelike_engine.console.console_model import CommandRegistry
    from roguelike_engine.console.commands import register_commands

    reg = CommandRegistry()
    register_commands(reg, fake_game_with_inventory)

    out, err = reg.execute('echo "hola"')
    assert err is None and out == "hola"

    assert "echo" in reg.autocomplete("e")
