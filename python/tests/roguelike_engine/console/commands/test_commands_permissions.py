import pytest


def test_register_commands_when_game_absent():
    from roguelike_engine.console.console_model import CommandRegistry
    from roguelike_engine.console.commands import register_commands

    reg = CommandRegistry()
    register_commands(reg, None)

    out, err = reg.execute("add inventory potion_small 1")
    assert err is None
    assert "Inventario no disponible" in out
