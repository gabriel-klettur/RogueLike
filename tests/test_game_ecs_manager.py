# Path: tests/test_game_ecs_manager.py
import sys
import os
sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), '..', 'src')))

import pytest
from roguelike_game.managers.ecs_manager import ECSManager
from roguelike_engine.config.map_config import global_map_settings

class DummySpawnNPCManager:
    def __init__(self):
        self.spawned = False

    def spawn_npc_initial(self):
        self.spawned = True

class DummyECSWorld:
    def __init__(self, screen, map_manager, buildings, perf_log):
        self.screen = screen
        self.map_manager = map_manager
        self.buildings = buildings
        self.perf_log = perf_log
        self.update_calls = []
        self.render_calls = []
        self.spawn_npc_manager = DummySpawnNPCManager()
        self.player_entity = None

    def update(self, camera):
        self.update_calls.append(camera)

    def render(self, screen, camera):
        self.render_calls.append((screen, camera))

class DummyMapManager:
    def __init__(self):
        self._local_state = {}
        self.lobby_offset = (0, 0)
        self.spawned_player_pos = None

    def spawn_player(self, pos):
        self.spawned_player_pos = pos

class DummyEntitiesManager:
    def __init__(self):
        self.buildings = {}
        self.ecs_manager = None

class DummyPerfLog:
    pass

@pytest.fixture(autouse=True)
def patch_resources(monkeypatch):
    import roguelike_game.managers.ecs_manager as em
    monkeypatch.setattr(em, 'ECSWorld', DummyECSWorld)
    def fake_spawn(esw, x, y):
        return 42
    monkeypatch.setattr(em, 'spawn_player_tile', fake_spawn)
    return

def test_get_initial_player_tile_saved():
    screen = None
    map_manager = DummyMapManager()
    map_manager._local_state['player_pos'] = (5, 6)
    entities_manager = DummyEntitiesManager()
    perf_log = DummyPerfLog()
    mgr = ECSManager(screen, map_manager, entities_manager, perf_log)
    assert mgr._get_initial_player_tile() == (5, 6)


def test_get_initial_player_tile_default(monkeypatch):
    monkeypatch.setattr(global_map_settings, 'zone_width', 8)
    monkeypatch.setattr(global_map_settings, 'zone_height', 6)
    screen = None
    map_manager = DummyMapManager()
    map_manager.lobby_offset = (1, 2)
    entities_manager = DummyEntitiesManager()
    perf_log = DummyPerfLog()
    mgr = ECSManager(screen, map_manager, entities_manager, perf_log)
    expected = (1 + 8 // 2, 2 + 6 // 2)
    assert mgr._get_initial_player_tile() == expected


def test_spawn_player_and_npc():
    screen = None
    map_manager = DummyMapManager()
    map_manager.lobby_offset = (2, 3)
    entities_manager = DummyEntitiesManager()
    perf_log = DummyPerfLog()
    mgr = ECSManager(screen, map_manager, entities_manager, perf_log)
    # spawn_player_tile returns 42
    assert mgr.ecs_world.player_entity == 42
    # map_manager.spawn_player called with tile
    assert map_manager.spawned_player_pos == mgr._get_initial_player_tile()
    # spawn_npc_initial called
    assert mgr.ecs_world.spawn_npc_manager.spawned


def test_update_and_render_calls():
    screen = object()
    map_manager = DummyMapManager()
    entities_manager = DummyEntitiesManager()
    perf_log = DummyPerfLog()
    mgr = ECSManager(screen, map_manager, entities_manager, perf_log)
    camera = object()
    mgr.update(None, screen, camera)
    assert mgr.ecs_world.update_calls == [camera]
    mgr.render(screen, camera)
    assert mgr.ecs_world.render_calls == [(screen, camera)]
