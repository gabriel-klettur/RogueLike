import pytest


def test_small_flow_inventory_and_quit(registry_with_game, patched_pygame):
    # Añadir, listar, editar, quitar
    out, err = registry_with_game.execute("add inventory weapons sword 2")
    assert err is None and "Añadidos 2x weapons_sword" in out

    out, _ = registry_with_game.execute("list inventory")
    assert "weapons_sword: 2" in out

    out, _ = registry_with_game.execute("edit inventory weapons sword quantity 3")
    assert "ajustada a 3" in out

    out, _ = registry_with_game.execute("remove inventory weapons sword 1")
    assert "Eliminados 1x weapons_sword" in out

    # Alias listitems
    out, _ = registry_with_game.execute("listitems inventory")
    assert "weapons_sword: 2" in out

    # quit debe postear pygame.QUIT
    out, _ = registry_with_game.execute("quit")
    import pygame
    assert any(e.type == pygame.QUIT for e in patched_pygame.posted)
