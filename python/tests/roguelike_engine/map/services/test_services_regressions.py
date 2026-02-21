import types
import roguelike_engine.map.services.expansion_service as svc


def test_next_zone_key_sequence_and_parent_chain(monkeypatch):
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
    monkeypatch.setattr(svc, 'random', types.SimpleNamespace(choice=lambda xs: 'right'))

    # Stub world
    world = types.SimpleNamespace()
    world.player_entity = 1
    world.player_position = types.SimpleNamespace(x=5.0, y=5.0)
    world.components = {'Position': {1: types.SimpleNamespace(x=0.0, y=0.0)}}
    class MapManager:
        lobby_offset = (0, 0)
        def expand_zone(self, side, new_key, parent_key):
            pass
    world.map_manager = MapManager()

    # First expand -> ('extra_dungeon', 'dungeon')
    k1, p1 = svc.expand_dungeon(world)
    # Second expand -> ('extra_dungeon2', 'extra_dungeon')
    k2, p2 = svc.expand_dungeon(world)
    # Third expand -> ('extra_dungeon3', 'extra_dungeon2')
    k3, p3 = svc.expand_dungeon(world)

    assert (k1, p1) == ('extra_dungeon', 'dungeon')
    assert (k2, p2) == ('extra_dungeon2', 'extra_dungeon')
    assert (k3, p3) == ('extra_dungeon3', 'extra_dungeon2')

    assert Settings.additional_zones[k2] == ('extra_dungeon', 'right')
    assert Settings.additional_zones[k3] == ('extra_dungeon2', 'right')
