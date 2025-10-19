import types
import roguelike_engine.map.services.expansion_service as svc


def test_expand_dungeon_without_player_position_uses_lobby_center(monkeypatch):
    # Settings: small grid with known offsets
    class Settings:
        zone_width = 6
        zone_height = 4
        additional_zones = {}
        zone_offsets = {
            'lobby': (3, 2),
            'dungeon': (9, 2),
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

    # Deterministic side selection
    monkeypatch.setattr(svc, 'random', types.SimpleNamespace(choice=lambda xs: xs[0]))

    # World stub without player_position triggers fallback path
    world = types.SimpleNamespace()
    world.player_entity = 1
    world.components = {'Position': {1: types.SimpleNamespace(x=0.0, y=0.0)}}
    class MapManager:
        lobby_offset = Settings.zone_offsets['lobby']
        def expand_zone(self, side, new_key, parent_key):
            # side must be a valid string
            assert side in ('bottom', 'top', 'left', 'right')
    world.map_manager = MapManager()

    new_key, parent_key = svc.expand_dungeon(world)

    assert parent_key == 'dungeon'
    assert new_key in Settings.additional_zones
