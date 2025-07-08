import sys
import pathlib
import json
import os
import pytest
from unittest import mock

# Asegurar que src esté en sys.path para importar módulos
ROOT = pathlib.Path(__file__).resolve().parents[1]
SRC = ROOT / "src"
sys.path.insert(0, str(SRC))

from roguelike_game.ecs.systems.inventory.inventory_init_system import InventoryInitSystem
from roguelike_game.ecs.components.inventory_component import InventoryComponent


def setup_default_files(tmp_path, default_monsters, default_player):
    defaults_dir = tmp_path / "data" / "defaults"
    active_dir = tmp_path / "data"
    defaults_dir.mkdir(parents=True)
    active_dir.mkdir(parents=True, exist_ok=True)
    (defaults_dir / "inventory_monsters.json").write_text(json.dumps(default_monsters))
    (defaults_dir / "inventory_player.json").write_text(json.dumps(default_player))
    return str(defaults_dir / "inventory_monsters.json"), str(defaults_dir / "inventory_player.json"), \
           str(active_dir / "inventory_monsters.json"), str(active_dir / "inventory_player.json")


def test_invalid_active_json(tmp_path, monkeypatch):
    # Crear defaults con plantillas vacías
    def default_monsters(): return {"goblin": {"template_id": "goblin", "inventory": []}}
    def default_player(): return {"player_id": "hero", "capacity": 5, "slots": []}
    dm, dp, am, ap = setup_default_files(tmp_path, default_monsters(), default_player())
    # Escribir archivos activos vacíos (invalid JSON)
    for path in [am, ap]:
        path = pathlib.Path(path)
        path.write_text("")
    # Instanciar sistema, debe recrear archivos activos sin excepción
    system = InventoryInitSystem(default_monster_path=dm,
                                 active_monster_path=am,
                                 default_player_path=dp,
                                 active_player_path=ap)
    # Simular world con un jugador para forzar update
    class DummyWorld:
        def __init__(self):
            self.components = {"PlayerTagComponent": {1: object()},
                               "InventoryComponent": {},
                               "NPCTagComponent": {},
                               "Identity": {}}
    world = DummyWorld()
    # No debe lanzar JSONDecodeError
    system.update(world)
    # Files exist and contienen un dict JSON válido
    assert json.loads(pathlib.Path(am).read_text()) == {}
    # El archivo de jugadores debería contener la entrada inicializada
    ap_data = json.loads(pathlib.Path(ap).read_text())
    assert '1' in ap_data
    entry = ap_data['1']
    assert entry['player_id'] == 'hero'
    assert entry['schema_version'] == '1.0.0'
    # slots debe ser lista de None de tamaño capacity (5)
    assert isinstance(entry['slots'], list)
    assert len(entry['slots']) == 5
    assert all(slot is None for slot in entry['slots'])
