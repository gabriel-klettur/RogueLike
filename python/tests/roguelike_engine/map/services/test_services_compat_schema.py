import types

import roguelike_engine.map.services.expansion_service as svc


def test_expand_dungeon_additional_zones_schema_and_values(monkeypatch):
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
    monkeypatch.setattr(svc.random, 'choice', lambda xs: xs[0] if xs else 'bottom', raising=True)

    world = types.SimpleNamespace(
        player_entity=None,
        player_position=None,
        components={'Position': {}},
        map_manager=types.SimpleNamespace(lobby_offset=(0, 0), expand_zone=lambda *a, **k: None),
    )

    # Expand twice
    k1, p1 = svc.expand_dungeon(world)
    k2, p2 = svc.expand_dungeon(world)

    # Keys are unique strings
    assert isinstance(k1, str) and isinstance(k2, str) and k1 != k2
    # Values in additional_zones follow (parent, side) and sides are valid
    parent, side = Settings.additional_zones[k1]
    assert parent == 'dungeon' and side in {'bottom', 'top', 'left', 'right'}
    parent2, side2 = Settings.additional_zones[k2]
    assert parent2 in {k1, 'dungeon'} and side2 in {'bottom', 'top', 'left', 'right'}
