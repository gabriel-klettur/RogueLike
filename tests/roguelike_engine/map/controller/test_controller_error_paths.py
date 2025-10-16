import pytest
import roguelike_engine.map.controller.map_service as ms


def test_build_map_loader_failure(monkeypatch):
    class Settings:
        zone_width = 4
        zone_height = 3
        global_width = 8
        global_height = 6
        dungeon_connect_side = "right"
        additional_zones = {}
        zone_offsets = {'lobby': (2, 1), 'dungeon': (6, 1)}
        @staticmethod
        def _dynamic_offsets():
            return {'lobby': (2, 1), 'dungeon': (6, 1)}

    monkeypatch.setattr(ms, 'global_map_settings', Settings, raising=True)

    class Gen:
        def generate(self, width, height, return_rooms=False):
            raw = [['.' for _ in range(width)] for _ in range(height)]
            return raw, {'rooms': [(0, 0, width - 1, height - 1)]}
    monkeypatch.setattr(ms, 'get_generator', lambda name: Gen(), raising=True)

    class Loader:
        def load(self, rows, key):
            raise ValueError("loader failure")
    monkeypatch.setattr(ms, 'get_map_loader', lambda name: Loader(), raising=True)

    monkeypatch.setattr(ms, 'generate_lobby_matrix', lambda: ['.' * Settings.zone_width for _ in range(Settings.zone_height)], raising=True)
    monkeypatch.setattr(ms, 'calculate_lobby_offset', lambda: Settings.zone_offsets['lobby'], raising=True)
    monkeypatch.setattr(ms, 'calculate_dungeon_offset', lambda off: Settings.zone_offsets['dungeon'], raising=True)

    with pytest.raises(ValueError):
        ms.MapService().build_map()
