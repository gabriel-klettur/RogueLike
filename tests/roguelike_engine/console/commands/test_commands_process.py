import pytest


def test_register_commands_registers_core_and_inventory(fake_game_with_inventory):
    from roguelike_engine.console.console_model import CommandRegistry
    from roguelike_engine.console.commands import register_commands

    reg = CommandRegistry()
    register_commands(reg, fake_game_with_inventory)

    out, err = reg.execute("echo ok")
    assert err is None and out == "ok"

    out, err = reg.execute("add inventory weapons sword 1")
    assert err is None
    assert "Añadidos 1x weapons_sword" in out
