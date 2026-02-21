import pygame
import types

from roguelike_editors.map.view.zones_view import ZonesView
from roguelike_editors.map.view.colliders_view import CollidersView


class CamTinyZoom:
    def __init__(self, zoom: float = 1e-9, offset_x: float = 0.0, offset_y: float = 0.0):
        self.zoom = float(zoom)
        self.offset_x = float(offset_x)
        self.offset_y = float(offset_y)

    def apply(self, pos):
        x, y = pos
        z = self.zoom or 1.0
        return int(round((x - self.offset_x) * z)), int(round((y - self.offset_y) * z))

    def scale(self, size):
        w, h = size
        return int(round(w * self.zoom)), int(round(h * self.zoom))


class PalStub:
    border_hidden = (128, 128, 128)
    border_selected = (255, 255, 0)
    border_default = (255, 255, 255)
    text = (255, 255, 255)
    collider_fill = (255, 0, 0, 50)
    collider_border = (255, 0, 0)


def test_zones_view_does_not_create_zero_sized_surfaces(monkeypatch):
    # Patch global_map_settings inside zones_view module to a tiny test map
    import roguelike_editors.map.view.zones_view as zv

    gms = types.SimpleNamespace(
        zone_offsets={"zoneA": (0, 0), "zoneB": (10, 10)},
        zone_size=(8, 6),
    )
    monkeypatch.setattr(zv, "global_map_settings", gms, raising=False)

    fonts = types.SimpleNamespace(large=pygame.font.SysFont(None, 16))
    palette = PalStub()

    zvw = ZonesView(fonts, palette)
    screen = pygame.Surface((640, 480))

    # State stub: minimal attributes referenced by ZonesView
    state = types.SimpleNamespace(
        hidden_zones=set(),
        selected_zone=None,
        pending_delete_zone=None,
        renaming_zone=None,
        rename_input="",
        rename_input_rect=None,
        rename_accept_rect=None,
    )

    cam = CamTinyZoom(zoom=1e-9)

    # Should not raise, even though scale() returns (0, 0) and code guards skip drawing
    zvw.render(screen, cam, state)


def test_colliders_view_skips_zero_sized_surfaces():
    palette = PalStub()
    cvw = CollidersView(palette)
    screen = pygame.Surface((640, 480))
    cam = CamTinyZoom(zoom=1e-9)

    # Solid tiles stub with a couple of positions
    Tile = lambda x, y: types.SimpleNamespace(x=x, y=y)
    map_manager = types.SimpleNamespace(solid_tiles=[Tile(0, 0), Tile(64, 64)])

    # Should not raise; guard will skip zero-sized overlay surfaces
    cvw.render(screen, cam, map_manager)
