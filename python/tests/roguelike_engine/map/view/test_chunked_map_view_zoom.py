import pygame
import pytest

from roguelike_engine.map.view.chunked_map_view import ChunkedMapView
from roguelike_engine.map.model.layer import Layer


class CamStub:
    def __init__(self, zoom: float, offset_x: float = 0.0, offset_y: float = 0.0):
        self.zoom = float(zoom)
        self.offset_x = float(offset_x)
        self.offset_y = float(offset_y)

    def apply(self, pos):
        x, y = pos
        # basic world->screen transform using zoom and offsets
        return int(round((x - self.offset_x) * self.zoom)), int(round((y - self.offset_y) * self.zoom))


class MapStub:
    """Minimal map-like object for ChunkedMapView.render()"""
    def __init__(self, w=8, h=8):
        # matrix: list of strings (characters per tile)
        self.matrix = ["." * w for _ in range(h)]
        # layers: dict[Layer, List[List[str]]] with codes per tile
        codes = [["floor" for _ in range(w)] for _ in range(h)]
        # provide only a couple of layers to keep it light
        self.layers = {
            Layer.Ground: codes,
            Layer.ObjectsLow: [["" for _ in range(w)] for _ in range(h)],
        }


@pytest.fixture(autouse=True)
def _pygame_init_teardown():
    pygame.init()
    try:
        yield
    finally:
        pygame.quit()


def _dummy_sprite(size=(16, 16), color=(0, 255, 0, 255)):
    surf = pygame.Surface(size, pygame.SRCALPHA)
    surf.fill(color)
    return surf


def test_render_clamps_zoom_and_builds_nonzero_chunks(monkeypatch):
    # Monkeypatch sprite loader used internally by ChunkedMapView
    import roguelike_engine.map.view.chunked_map_view as cmv

    monkeypatch.setattr(cmv, "get_sprite_for_tile", lambda char, code: _dummy_sprite())

    screen = pygame.Surface((320, 240))
    mv = ChunkedMapView(chunk_size=8)
    m = MapStub(w=16, h=16)

    # Try extreme zooms; render() should clamp to [0.1 .. MAX_ZOOM] and not crash
    for z in (1e-12, 1e-6, 0.05, 0.1, 1.0, 10.0, 1000.0):
        cam = CamStub(zoom=z)
        dirty = mv.render(screen, cam, m)
        assert isinstance(dirty, list)
        # There must be a cache for the clamped zoom
        assert mv.chunks_by_zoom
        # Fetch the only/last zoom key used
        last_zoom_key = list(mv.chunks_by_zoom.keys())[-1]
        assert 0.1 <= last_zoom_key <= cmv.MAX_ZOOM
        # All generated chunk surfaces must have width/height >= 1
        for surf in mv.chunks_by_zoom[last_zoom_key].values():
            w, h = surf.get_size()
            assert w >= 1 and h >= 1
