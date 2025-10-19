import types

import roguelike_engine.map.services.expansion_service as svc


def test_choose_side_returns_bottom_when_no_valid_sides(monkeypatch):
    # Settings with all four sides occupied around the parent
    class Settings:
        zone_width = 10
        zone_height = 8
        additional_zones = {}
        zone_offsets = {
            'dungeon': (0, 0),
            'left_zone': (-10, 0),
            'right_zone': (10, 0),
            'top_zone': (0, -8),
            'bottom_zone': (0, 8),
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

    # If all sides are used, _choose_side should default to 'bottom'
    side = svc._choose_side('dungeon')
    assert side == 'bottom'


def test_expand_dungeon_without_player_position(monkeypatch):
    # Settings with lobby offset and no additional zones used yet
    class Settings:
        zone_width = 10
        zone_height = 8
        additional_zones = {}
        zone_offsets = {'dungeon': (2, 3)}
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
    monkeypatch.setattr(svc.random, 'choice', lambda xs: xs[0] if xs else 'bottom', raising=True)

    # World without player_position and without Position component
    world = types.SimpleNamespace()
    world.map_manager = types.SimpleNamespace(lobby_offset=(0, 0), expand_zone=lambda *a, **k: None)
    world.components = {'Position': {}}
    world.player_entity = None

    new_key, parent_key = svc.expand_dungeon(world)
    assert parent_key == 'dungeon'
    assert new_key in Settings.additional_zones
    # No Position component to update; nothing should crash
