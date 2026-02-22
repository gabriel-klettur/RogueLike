import json
import types
import roguelike_engine.map.services.expansion_service as svc


def test_additional_zones_json_serializable(monkeypatch):
    class Settings:
        zone_width = 8
        zone_height = 6
        additional_zones = {}
        zone_offsets = {'lobby': (0, 0), 'dungeon': (8, 0)}
        @staticmethod
        def calculate_offset(parent_off, side):
            x, y = parent_off
            return {
                'right': (x + Settings.zone_width, y),
                'left': (x - Settings.zone_width, y),
                'top': (x, y - Settings.zone_height),
                'bottom': (x, y + Settings.zone_height),
            }[side]
    monkeypatch.setattr(svc, 'global_map_settings', Settings, raising=True)
    monkeypatch.setattr(svc, 'random', types.SimpleNamespace(choice=lambda xs: xs[0]))

    world = types.SimpleNamespace()
    world.player_entity = 1
    world.player_position = types.SimpleNamespace(x=5.0, y=5.0)
    world.components = {'Position': {1: types.SimpleNamespace(x=0.0, y=0.0)}}
    class MapManager:
        lobby_offset = (0, 0)
        def expand_zone(self, side, new_key, parent_key):
            pass
    world.map_manager = MapManager()

    new_key, parent_key = svc.expand_dungeon(world)

    # Should be JSON serializable (tuples become lists in JSON)
    dumped = json.dumps(Settings.additional_zones)
    assert isinstance(dumped, str) and new_key in dumped and parent_key in dumped
