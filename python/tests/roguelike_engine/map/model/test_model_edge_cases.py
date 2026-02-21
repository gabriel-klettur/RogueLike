import types
import roguelike_engine.map.model.map_model as mm
from roguelike_engine.map.model.layer import Layer


def _fake_loader(matrix, codes=None):
    h = len(matrix)
    w = len(matrix[0]) if h else 0
    return [[types.SimpleNamespace(x=x, y=y, code=(codes[y][x] if codes else '')) for x in range(w)] for y in range(h)]


def test_get_tile_out_of_bounds_returns_none(monkeypatch):
    monkeypatch.setattr(mm, 'load_tiles_from_text', _fake_loader, raising=True)

    matrix = ["..", ".."]
    ground = [["g" for _ in range(2)] for _ in range(2)]
    tiles_by_layer = {Layer.Ground: _fake_loader(matrix, ground)}
    m = mm.Map(matrix=matrix, layers={Layer.Ground: ground}, tiles_by_layer=tiles_by_layer, metadata={}, name="m")

    assert m.get_tile(Layer.Ground, -1, 0) is None
    assert m.get_tile(Layer.Ground, 2, 0) is None
    assert m.get_tile(Layer.Ground, 0, 2) is None


def test_get_tiles_for_missing_layer_returns_empty(monkeypatch):
    monkeypatch.setattr(mm, 'load_tiles_from_text', _fake_loader, raising=True)

    matrix = [".."]
    ground = [["g", "g"]]
    tiles_by_layer = {Layer.Ground: _fake_loader(matrix, ground)}
    m = mm.Map(matrix=matrix, layers={Layer.Ground: ground}, tiles_by_layer=tiles_by_layer, metadata={}, name="m")

    assert m.get_tiles_for_layer(Layer.Decorations) == []


def test_set_tile_out_of_bounds_no_crash(monkeypatch):
    monkeypatch.setattr(mm, 'load_tiles_from_text', _fake_loader, raising=True)

    matrix = ["..", ".."]
    ground = [["a", "b"], ["c", "d"]]
    tiles_by_layer = {Layer.Ground: _fake_loader(matrix, ground)}
    m = mm.Map(matrix=matrix, layers={Layer.Ground: [row[:] for row in ground]}, tiles_by_layer=tiles_by_layer, metadata={}, name="m")

    # Out-of-range indices should be ignored without raising
    m.set_tile(Layer.Ground, 5, 5, "Z")
    assert m.overlay[1][1] == "d"
