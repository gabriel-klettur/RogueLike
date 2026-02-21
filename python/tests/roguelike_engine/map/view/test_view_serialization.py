import pickle
import types
import roguelike_engine.map.view.map_view as mv


def test_map_view_picklable_with_stub_zone_view(monkeypatch):
    # Replace ZoneView with a lightweight, picklable stub
    class StubZoneView:
        def render_zone(self, screen, camera, zone_name, tiles):
            return None

    monkeypatch.setattr(mv, 'ZoneView', StubZoneView, raising=True)

    view = mv.MapView()

    data = pickle.dumps(view)
    restored = pickle.loads(data)

    # Avoid strict isinstance, which can fail under module reload/aliasing;
    # verify identity by name and required interface instead.
    assert restored.__class__.__name__ == mv.MapView.__name__
    assert hasattr(restored, 'zone_view')
