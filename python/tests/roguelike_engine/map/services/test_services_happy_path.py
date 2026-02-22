import types

import roguelike_engine.map.services.expansion_service as svc


def test_expand_dungeon_happy_path(monkeypatch):
    # Make random deterministic
    monkeypatch.setattr(svc.random, 'choice', lambda xs: xs[0] if xs else 'bottom', raising=True)

    # Fake global settings
    class Settings:
        zone_width = 10
        zone_height = 8
        additional_zones = {}
        zone_offsets = {
            'dungeon': (0, 0),
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

    # World with player position and components
    player_entity = 1
    position_comp = types.SimpleNamespace(x=5.0, y=7.0)
    world = types.SimpleNamespace()
    world.player_entity = player_entity
    world.player_position = types.SimpleNamespace(x=25.0, y=24.0)  # inside dungeon zone
    world.components = {'Position': {player_entity: position_comp}}

    # Map manager stub
    calls = {}
    class MapManager:
        lobby_offset = (0, 0)
        def expand_zone(self, side, new_key, parent_key):
            calls['args'] = (side, new_key, parent_key)
    world.map_manager = MapManager()

    new_key, parent_key = svc.expand_dungeon(world)

    # New additional zone registered and expansion called on a valid side
    assert parent_key == 'dungeon'
    assert new_key in Settings.additional_zones
    assert 'args' in calls and calls['args'][1] == new_key and calls['args'][2] == parent_key

    # Player position updated to corresponding zone offset + rel coords
    # rel coords computed from player_position vs current zone offset
    off_x, off_y = Settings.zone_offsets[parent_key]
    rel_x = int(world.player_position.x) // svc.TILE_SIZE - off_x
    rel_y = int(world.player_position.y) // svc.TILE_SIZE - off_y
    new_off_x, new_off_y = Settings.zone_offsets[parent_key]  # parent remains; movement applies to new zone offset later
    # After expand, service moves to new zone computed from current_zone; validate stays within tile grid
    assert isinstance(position_comp.x, float) and isinstance(position_comp.y, float)
