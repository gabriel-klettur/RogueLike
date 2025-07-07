import sys
import pathlib
import json
import pygame
import pytest

# Asegurar que src esté en sys.path para importar módulos
ROOT = pathlib.Path(__file__).resolve().parents[1]
SRC = ROOT / "src"
sys.path.insert(0, str(SRC))

from roguelike_game.config.input_config import InputConfig


def test_get_key_drop(tmp_path):
    config_file = tmp_path / "input_bindings.json"
    # Instanciar con un path no existente crea archivo con bindings por defecto
    config = InputConfig(path=str(config_file))
    assert config_file.exists()
    # get_key para 'drop' debe devolver pygame.K_d
    assert config.get_key("drop") == pygame.K_d
