import roguelike_engine.map.services.expansion_service as svc


def test_next_zone_key_progression(monkeypatch):
    class Settings:
        additional_zones = {
            'extra_dungeon': ('dungeon', 'bottom'),
            'extra_dungeon2': ('extra_dungeon', 'right'),
            'extra_dungeon3': ('extra_dungeon2', 'right'),
        }
    monkeypatch.setattr(svc, 'global_map_settings', Settings, raising=True)

    # Expect next key to be 'extra_dungeon4' and parent 'extra_dungeon3'
    new_key, parent = svc._next_zone_key()
    assert new_key == 'extra_dungeon4'
    assert parent == 'extra_dungeon3'


def test_choose_side_avoids_used_offsets(monkeypatch):
    class Settings:
        zone_width = 10
        zone_height = 8
        # Parent at (0,0); mark all but 'top' as used
        zone_offsets = {
            'dungeon': (0, 0),
            'right_zone': (10, 0),
            'left_zone': (-10, 0),
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
    # Deterministic choice
    monkeypatch.setattr(svc.random, 'choice', lambda xs: xs[0], raising=True)

    side = svc._choose_side('dungeon')
    # Only 'top' is free
    assert side == 'top'
