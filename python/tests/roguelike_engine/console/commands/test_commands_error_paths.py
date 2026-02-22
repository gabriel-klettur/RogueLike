import pytest


def test_unknown_command_message():
    from roguelike_engine.console.console_model import CommandRegistry
    from roguelike_engine.console.commands import register_commands

    reg = CommandRegistry()
    register_commands(reg, None)

    out, err = reg.execute("unknown")
    assert err is None and out.startswith("Unknown command:")
