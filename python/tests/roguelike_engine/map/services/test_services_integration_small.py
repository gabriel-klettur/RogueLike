import types

import roguelike_engine.map.services.expansion_service as svc
from roguelike_engine.config.config_tiles import TILE_SIZE


def test_expand_dungeon_preserves_player_world_position_and_registers_zone(monkeypatch):
    # Global settings with base zones and deterministic side choice
    class Settings:
        zone_width = 10
        zone_height = 8
        additional_zones = {}
        zone_offsets = {
            'lobby': (0, 0),
            'dungeon': (20, 10),  # tile offsets for dungeon
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
    monkeypatch.setattr(svc.random, 'choice', lambda xs: xs[0] if xs else 'bottom', raising=True)

    # World with player inside 'dungeon' zone in tile-space
    player_eid = 1
    # Choose a tile inside dungeon and a subpixel remainder
    rel_tx, rel_ty = 3, 2
    px = (Settings.zone_offsets['dungeon'][0] + rel_tx) * TILE_SIZE + 0.25
    py = (Settings.zone_offsets['dungeon'][1] + rel_ty) * TILE_SIZE + 0.75

    pos_comp = types.SimpleNamespace(x=px, y=py)

    world = types.SimpleNamespace()
    world.player_entity = player_eid
    world.player_position = types.SimpleNamespace(x=px, y=py)
    world.components = {
        'Position': {player_eid: pos_comp}
    }

    calls = {}
    class MapManager:
        lobby_offset = (0, 0)
        def expand_zone(self, side, new_key, parent_key):
            calls['args'] = (side, new_key, parent_key)
    world.map_manager = MapManager()

    new_key, parent_key = svc.expand_dungeon(world)

    # New zone registered with a valid side
    assert parent_key == 'dungeon'
    assert new_key in Settings.additional_zones
    assert 'args' in calls and calls['args'][2] == parent_key

    # Position component preserved in world coordinates (same pixel values)
    assert pos_comp.x == px and pos_comp.y == py
