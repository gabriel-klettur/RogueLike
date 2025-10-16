import pytest


def test_help_text_via_aggregator(fake_game_with_inventory):
    from roguelike_engine.console.console_model import CommandRegistry
    from roguelike_engine.console.commands import register_commands

    reg = CommandRegistry()
    register_commands(reg, fake_game_with_inventory)

    out, err = reg.execute("help help")
    assert err is None
    assert "Uso:" in out and "help" in out
