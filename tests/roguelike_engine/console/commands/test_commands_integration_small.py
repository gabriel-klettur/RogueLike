import pytest


def test_small_integration_via_aggregator(fake_game_with_inventory, patched_pygame):
    from roguelike_engine.console.console_model import CommandRegistry
    from roguelike_engine.console.commands import register_commands
    import pygame

    reg = CommandRegistry()
    register_commands(reg, fake_game_with_inventory)

    out, _ = reg.execute("echo hi")
    assert out == "hi"

    reg.execute("quit")
    assert any(e.type == pygame.QUIT for e in patched_pygame.posted)
