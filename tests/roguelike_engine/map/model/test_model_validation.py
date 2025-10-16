import types
import roguelike_engine.map.model.map_model as mm
from roguelike_engine.map.model.layer import Layer


def _fake_loader(matrix, codes=None):
    h = len(matrix)
    w = len(matrix[0]) if h else 0
    return [[types.SimpleNamespace(x=x, y=y, code=(codes[y][x] if codes else '')) for x in range(w)] for y in range(h)]


def test_set_tile_ignores_missing_layer_and_invalid_indices(monkeypatch):
    monkeypatch.setattr(mm, 'load_tiles_from_text', _fake_loader, raising=True)

    matrix = ["....", "...."]
    ground = [["g" for _ in range(4)] for _ in range(2)]
    tiles_by_layer = {Layer.Ground: _fake_loader(matrix, ground)}

    m = mm.Map(matrix=matrix, layers={Layer.Ground: [row[:] for row in ground]}, tiles_by_layer=tiles_by_layer, metadata={}, name="m")

    # Missing layer -> no exception and no mutation
    before = [row[:] for row in m.overlay]
    m.set_tile(Layer.Decorations, 0, 0, "D")
    assert m.overlay == before

    # Invalid indices -> no exception and overlay unchanged
    m.set_tile(Layer.Ground, 999, 999, "Z")
    assert m.overlay == before


def test_get_tile_validation(monkeypatch):
    monkeypatch.setattr(mm, 'load_tiles_from_text', _fake_loader, raising=True)

    matrix = ["..", ".."]
    ground = [["g", "g"], ["g", "g"]]
    tiles_by_layer = {Layer.Ground: _fake_loader(matrix, ground)}
    m = mm.Map(matrix=matrix, layers={Layer.Ground: ground}, tiles_by_layer=tiles_by_layer, metadata={}, name="m")

    assert m.get_tile(Layer.Ground, 1, 1)
    # Invalid coords -> None
    assert m.get_tile(Layer.Ground, -1, 0) is None
    assert m.get_tile(Layer.Ground, 0, -1) is None
    assert m.get_tile(Layer.Ground, 2, 0) is None
    assert m.get_tile(Layer.Ground, 0, 2) is None
