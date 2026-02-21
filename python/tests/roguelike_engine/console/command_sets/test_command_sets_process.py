import pytest


def test_echo_and_godmode_process(registry_with_game):
    out, err = registry_with_game.execute("echo hola mundo")
    assert err is None
    assert out == "hola mundo"

    # godmode toggle: debe alternar estado en game.state o world.state
    out, err = registry_with_game.execute("godmode on")
    assert err is None
    assert "godmode on" in out


def test_inventory_add_list_process(registry_with_game):
    out, err = registry_with_game.execute("add inventory weapons sword 2")
    assert err is None
    assert "Añadidos 2x weapons_sword" in out

    out, err = registry_with_game.execute("list inventory")
    assert err is None
    assert "weapons_sword: 2" in out


def test_inventory_remove_edit_process(registry_with_game):
    registry_with_game.execute("add inventory weapons sword 2")

    out, err = registry_with_game.execute("edit inventory weapons sword quantity 5")
    assert err is None
    assert "weapons_sword cantidad ajustada a 5" in out

    out, err = registry_with_game.execute("remove inventory weapons sword 3")
    assert err is None
    assert "Eliminados 3x weapons_sword" in out

    out, _ = registry_with_game.execute("list inventory")
    assert "weapons_sword: 2" in out
