import types
import roguelike_engine.map.view.map_view as mv


def test_render_visibility_and_dirty_rect_bounding_box(monkeypatch):
    # Fake pygame and ZoneView
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
            # No-op: we only care about dirty rect result
            return None
    monkeypatch.setattr(mv, 'ZoneView', StubZoneView, raising=True)

    # Fix TILE_SIZE to a known value
    monkeypatch.setattr(mv, 'TILE_SIZE', 16, raising=True)

    view = mv.MapView()

    # Camera: everything is visible; apply is identity
    camera = types.SimpleNamespace(
        is_in_view=lambda x, y, size: True,
        apply=lambda pos: pos,
    )

    # Two tiles: expect bounding box from (0,0) to (32+16, 16+16) => size (48, 32)
    tiles = [types.SimpleNamespace(x=0, y=0), types.SimpleNamespace(x=32, y=16)]
    map_manager = types.SimpleNamespace(tiles_by_zone={'Z': tiles})

    screen = FakePygame.Surface()

    dirty = view.render(screen, camera, map_manager)

    assert isinstance(dirty, list) and len(dirty) == 1
    rect = dirty[0]
    assert rect.topleft == (0, 0)
    assert rect.size == (48, 32)
