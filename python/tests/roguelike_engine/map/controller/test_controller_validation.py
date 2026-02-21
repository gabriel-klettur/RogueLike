import pytest
import roguelike_engine.map.controller.map_controller as mc
import roguelike_engine.map.controller.map_service as ms


def test_build_map_missing_offsets_raises(monkeypatch):
    class Settings:
        zone_width = 4
        zone_height = 4
        global_width = 8
        global_height = 8
        dungeon_connect_side = "right"
        additional_zones = {}
        zone_offsets = {}  # missing both lobby and dungeon
        @staticmethod
        def _dynamic_offsets():
            return {'lobby': (2, 2), 'dungeon': (6, 2)}

    monkeypatch.setattr(ms, 'global_map_settings', Settings, raising=True)

    with pytest.raises(KeyError):
        ms.MapService().build_map()
