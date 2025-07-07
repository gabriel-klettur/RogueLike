import os
import json
import tempfile
import pytest
from collections import defaultdict

import pygame

from roguelike_game.ecs.systems.inventory.map_load_drops_system import MapLoadDropsSystem
from roguelike_game.ecs.systems.input.input_system import InputSystem
from roguelike_game.ecs.components.input_component import InputComponent
from roguelike_game.ecs.components.transform.z_layer import ZLayer

# Dummy world for map_load_drops_system tests
class DummyMapManager:
    def get_spawn_pixel(self, coords):
        # return coords as-is for simplicity
        return coords

class DummyWorld:
    def __init__(self):
        # components default to dict-of-dict
        self.components = defaultdict(dict)
        self.map_manager = DummyMapManager()
        self._next_id = 1
    def create_entity(self):
        eid = self._next_id
        self._next_id += 1
        return eid

@pytest.fixture(autouse=True)
def patch_pygame(monkeypatch):
    # Ensure pygame key/mouse funcs exist
    monkeypatch.setattr(pygame.key, 'get_mods', lambda: 0)
    monkeypatch.setattr(pygame.key, 'get_pressed', lambda: [False])
    monkeypatch.setattr(pygame.mouse, 'get_pressed', lambda: [False, False, False])

class DummyConfig:
    def _load(self):
        pass
    def get_key(self, name):
        return 0

# Tests for InputSystem.show_all_drops flag
def test_input_system_sets_show_all_drops_true(monkeypatch):
    world = type('W', (), {})()
    world.components = {'InputComponent': {1: InputComponent()}}
    # override config and pygame.key.get_mods to ALT
    input_sys = InputSystem(None)
    input_sys.config = DummyConfig()
    monkeypatch.setattr(pygame.key, 'get_mods', lambda: pygame.KMOD_ALT)
    # call update
    input_sys.update(world)
    assert world.components['InputComponent'][1].show_all_drops is True

def test_input_system_sets_show_all_drops_false(monkeypatch):
    world = type('W', (), {})()
    world.components = {'InputComponent': {1: InputComponent()}}
    input_sys = InputSystem(None)
    input_sys.config = DummyConfig()
    monkeypatch.setattr(pygame.key, 'get_mods', lambda: 0)
    input_sys.update(world)
    assert world.components['InputComponent'][1].show_all_drops is False

# Tests for MapLoadDropsSystem incremental loading
@pytest.mark.skip
@pytest.mark.slow
def test_map_load_drops_system_incremental(tmp_path):
    # prepare temp JSON file
    data_file = tmp_path / "drops.json"
    # initial drops
    initial = {
        "d1": {"item_id": "gold", "quantity": 1, "zone_id": "Z", "schema_version": "1.0.0", "position": {"x": 5, "y": 5}},
        "d2": {"item_id": "wood", "quantity": 2, "zone_id": "Z", "schema_version": "1.0.0", "position": {"x": 10, "y": 10}}
    }
    data_file.write_text(json.dumps(initial))

    world = DummyWorld()
    system = MapLoadDropsSystem(None)
    # override path and items
    system.drop_manager.path = str(data_file)
    system.items = {}  # no models

    # first update: spawn both
    system.update(world)
    assert len(world.components['PhysicalItemComponent']) == 2
    spawned_ids = set(world.components['PhysicalItemComponent'].keys())
    # second update: no new
    system.update(world)
    assert len(world.components['PhysicalItemComponent']) == 2

    # add a new drop
    updated = dict(initial)
    updated["d3"] = {"item_id": "orb", "quantity": 3, "zone_id": "Z", "schema_version": "1.0.0", "position": {"x": 20, "y": 20}}
    data_file.write_text(json.dumps(updated))
    # third update: one more spawn
    system.update(world)
    assert len(world.components['PhysicalItemComponent']) == 3
    # verify new eid for d3
    # map drop_id to eids via physical components
    ids = []
    for eid, comp in world.components['PhysicalItemComponent'].items():
        if comp.item_id == 'orb':
            ids.append(eid)
    assert len(ids) == 1
    # test invalid JSON does not raise
    data_file.write_text("INVALID JSON")
    # should not raise
    system.update(world)
