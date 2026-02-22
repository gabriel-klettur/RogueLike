import types
import roguelike_engine.map.view.map_view as mv


def test_map_view_zoneview_signature_compat(monkeypatch):
    calls = {}

    class FakePygame:
        class Rect:
            def __init__(self, topleft, size):
                self.topleft = topleft
                self.size = size
        class Surface:
            pass
    monkeypatch.setattr(mv, 'pygame', FakePygame, raising=True)

    class StubZoneView:
        def render_zone(self, screen, camera, zone_name, tiles):
            calls['last'] = (screen, camera, zone_name, tuple((t.x, t.y) for t in tiles))

    monkeypatch.setattr(mv, 'ZoneView', StubZoneView, raising=True)

    view = mv.MapView()

    # Prepare camera and map_manager stubs
    camera = types.SimpleNamespace(
        is_in_view=lambda x, y, size: True,
        apply=lambda pos: pos,
    )
    tile = types.SimpleNamespace(x=0, y=0)
    map_manager = types.SimpleNamespace(tiles_by_zone={'lobby': [tile]})

    screen = FakePygame.Surface()
    dirty = view.render(screen, camera, map_manager)

    assert 'last' in calls
    scr, cam, zone_name, tiles = calls['last']
    assert zone_name == 'lobby'
    assert tiles == ((0, 0),)
    assert isinstance(dirty, list)
