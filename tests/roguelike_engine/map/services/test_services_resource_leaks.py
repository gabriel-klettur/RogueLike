import types
import roguelike_engine.map.services.expansion_service as svc


def test_expand_dungeon_does_not_mutate_zone_offsets(monkeypatch):
    # Baseline settings
    class Settings:
        zone_width = 10
        zone_height = 8
        additional_zones = {}
        zone_offsets = {
            'lobby': (0, 0),
            'dungeon': (10, 0),
        }
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

    # World stub
    world = types.SimpleNamespace()
    world.player_entity = 1
    world.player_position = types.SimpleNamespace(x=5.0, y=5.0)
    world.components = {'Position': {1: types.SimpleNamespace(x=0.0, y=0.0)}}
    class MapManager:
        lobby_offset = (0, 0)
        def expand_zone(self, side, new_key, parent_key):
            pass
    world.map_manager = MapManager()

    before = dict(Settings.zone_offsets)
    svc.expand_dungeon(world)
    after = dict(Settings.zone_offsets)

    # The service must not add or modify entries in zone_offsets (only additional_zones)
    assert after == before
