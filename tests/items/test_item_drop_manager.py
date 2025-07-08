import os
import tempfile
import json
import pytest
from roguelike_game.managers.map.item_drop_manager import ItemDropManager


def test_create_and_load(tmp_path):
    # Crea un archivo JSON en tmp
    path = tmp_path / "test_map.json"
    mgr = ItemDropManager(str(path))
    # Inicialmente vacío
    assert mgr.load_all() == []
    # Crear drop
    mgr.create_drop("d1", "gold", 3, "zone1", {"x": 1, "y": 2})
    data = json.loads(path.read_text(encoding='utf-8'))
    assert "d1" in data
    assert data["d1"]["item_id"] == "gold"
    assert data["d1"]["zone_id"] == "zone1"
    assert data["d1"]["tile"] == {"x": 1, "y": 2}
    # Cargar todos
    drops = mgr.load_all()
    assert isinstance(drops, list)
    assert drops[0]["quantity"] == 3
    assert drops[0]["zone_id"] == "zone1"
    assert drops[0]["tile"] == {"x": 1, "y": 2}


def test_pick_up(tmp_path):
    path = tmp_path / "test_map.json"
    mgr = ItemDropManager(str(path))
    mgr.create_drop("d2", "wood", 5, "zone2", {"x": 5, "y": 6})
    assert mgr.pick_up("d2") is True
    assert mgr.load_all() == []
    # Pick up no existente
    assert mgr.pick_up("nope") is False


def test_create_with_position(tmp_path):
    path = tmp_path / "test_map.json"
    mgr = ItemDropManager(str(path))
    # Crear drop con posición relativa
    mgr.create_drop("d3", "potion", 2, "zone3", position={"x": 0.5, "y": 0.25})
    data = json.loads(path.read_text(encoding='utf-8'))
    assert "d3" in data
    assert data["d3"]["item_id"] == "potion"
    assert data["d3"]["zone_id"] == "zone3"
    assert data["d3"]["position"] == {"x": 0.5, "y": 0.25}
    # no se crea tile
    assert "tile" not in data["d3"]
    drops = mgr.load_all()
    # Comprueba posicion en load_all
    assert isinstance(drops, list)
    assert drops[0]["position"] == {"x": 0.5, "y": 0.25}
