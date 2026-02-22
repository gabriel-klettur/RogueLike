import time
import types
import roguelike_engine.map.model.map_model as mm
from roguelike_engine.map.model.layer import Layer


def _fake_loader(matrix, codes=None):
    h = len(matrix)
    w = len(matrix[0]) if h else 0
    return [[types.SimpleNamespace(x=x, y=y, code=(codes[y][x] if codes else '')) for x in range(w)] for y in range(h)]


def test_map_access_perf_budget(monkeypatch):
    monkeypatch.setattr(mm, 'load_tiles_from_text', _fake_loader, raising=True)

    # Medium grid
    w, h = 64, 48
    matrix = ['.' * w for _ in range(h)]
    ground = [["g" for _ in range(w)] for _ in range(h)]
    tiles_by_layer = {Layer.Ground: _fake_loader(matrix, ground)}
    m = mm.Map(matrix=matrix, layers={Layer.Ground: ground}, tiles_by_layer=tiles_by_layer, metadata={}, name="m")

    start = time.perf_counter()
    # Exercise getters heavily
    for y in range(0, h, 2):
        for x in range(0, w, 2):
            _ = m.get_tile(Layer.Ground, x, y)
    elapsed_ms = (time.perf_counter() - start) * 1000.0

    assert elapsed_ms < 50.0
