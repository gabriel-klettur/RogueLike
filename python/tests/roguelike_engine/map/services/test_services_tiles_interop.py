import types

import roguelike_engine.map.services.expansion_service as svc


def test_expand_dungeon_produces_zone_key_and_free_side_for_tiles_merge(monkeypatch):
    # Settings with only 'right' occupied around dungeon; others free
    class Settings:
        zone_width = 5
        zone_height = 5
        additional_zones = {}
        zone_offsets = {
            'dungeon': (10, 10),
            # Occupy the RIGHT neighbor so _choose_side won't pick it
            'occupied_right': (15, 10),
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
    # Deterministic choice (first free side from list: bottom, top, left)
    monkeypatch.setattr(svc.random, 'choice', lambda xs: xs[0] if xs else 'bottom', raising=True)

    # Minimal world
    world = types.SimpleNamespace()
    world.player_entity = None
    world.player_position = None
    world.components = {'Position': {}}

    called = {}
    class MapManager:
        lobby_offset = (0, 0)
        def expand_zone(self, side, new_key, parent_key):
            called['side'] = side
            called['key'] = new_key
            called['parent'] = parent_key
    world.map_manager = MapManager()

    new_key, parent = svc.expand_dungeon(world)

    # New zone key follows expected naming and is not in zone_offsets yet
    assert isinstance(new_key, str) and new_key not in Settings.zone_offsets
    # Registered in additional_zones for loader/overlay phases to consume later
    assert new_key in Settings.additional_zones
    # Chosen side is free (not 'right' which was occupied)
    assert called['side'] in ('bottom', 'top', 'left') and called['side'] != 'right'
