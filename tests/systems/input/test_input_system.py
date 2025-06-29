# Path: tests/systems/input/test_input_system.py
import pytest
from roguelike_game.ecs.systems.input.input_system import InputSystem


def test_input_system_instantiation():
    sys = InputSystem(None, config_path=None)
    assert isinstance(sys, InputSystem), "Debe instanciar InputSystem correctamente"