import types

import roguelike_engine.map.services.expansion_service as svc


def test_expand_dungeon_repeated_calls_add_one_zone_each(monkeypatch):
    class Settings:
        zone_width = 10
        zone_height = 8
        additional_zones = {}
        zone_offsets = {'dungeon': (0, 0)}
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
    # Deterministic side to avoid randomness in counts
    monkeypatch.setattr(svc.random, 'choice', lambda xs: xs[0] if xs else 'bottom', raising=True)

    # Minimal world
    world = types.SimpleNamespace()
    world.player_entity = None
    world.player_position = None
    world.components = {'Position': {}}
    class MapManager:
        lobby_offset = (0, 0)
        def expand_zone(self, side, new_key, parent_key):
            pass
    world.map_manager = MapManager()

    # Call expand N times and ensure exactly N unique zones are added
    N = 10
    keys = set()
    for _ in range(N):
        new_key, parent = svc.expand_dungeon(world)
        keys.add(new_key)
    assert len(keys) == N
    assert len(Settings.additional_zones) == N
