import sys
import types
import importlib
import pygame


def test_map_view_imports_with_fake_zoneview_and_renders(monkeypatch):
    # Inject a fake ZoneView into the expected import path before importing map_view
    class DummyZoneView:
        def __init__(self):
            self.calls = []
        def render_zone(self, screen, camera, zone_name, tiles):
            self.calls.append((zone_name, len(tiles)))

    fake_mod = types.SimpleNamespace(ZoneView=DummyZoneView)
    monkeypatch.setitem(sys.modules, 'roguelike_engine.zone.zone_view', fake_mod)

    # Ensure a fresh import of map_view to pick up our fake
    sys.modules.pop('roguelike_engine.map.view.map_view', None)
    mv = importlib.import_module('roguelike_engine.map.view.map_view')

    MapView = mv.MapView
    view = MapView()
    # Access injected zone_view to validate calls
    assert isinstance(view.zone_view, DummyZoneView)

    # Build a simple camera and map_manager
    camera = types.SimpleNamespace(
        is_in_view=lambda x, y, size: True,
        apply=lambda pos: pos,
    )
    # 2 zones with simple tile-like objects
    Tile = types.SimpleNamespace
    tiles_by_zone = {
        'A': [Tile(x=0, y=0), Tile(x=32, y=0)],
        'B': [Tile(x=0, y=32)],
    }
    map_manager = types.SimpleNamespace(tiles_by_zone=tiles_by_zone)
    screen = pygame.Surface((100, 100))

    dirty = view.render(screen, camera, map_manager)

    # We drew something, and zone_view was called for each zone
    assert len(dirty) >= 2
    assert len(view.zone_view.calls) == 2
