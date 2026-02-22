import types

import roguelike_engine.map.model.map_model as mm
from roguelike_engine.map.model.layer import Layer


def test_get_tile_out_of_bounds_and_set_tile_ignores_outside(monkeypatch):
    def fake_loader(matrix, codes=None):
        h = len(matrix)
        w = len(matrix[0]) if h else 0
        return [[types.SimpleNamespace(x=x, y=y, code=(codes[y][x] if codes else "")) for x in range(w)] for y in range(h)]

    monkeypatch.setattr(mm, 'load_tiles_from_text', fake_loader, raising=True)

    matrix = ["."*2]
    layer_codes = [["a", "b"]]
    tiles_by_layer = {Layer.Ground: fake_loader(matrix, layer_codes)}
    m = mm.Map(matrix=matrix, layers={Layer.Ground: [row[:] for row in layer_codes]}, tiles_by_layer=tiles_by_layer, metadata={}, name="test")

    # Out-of-bounds lookups return None
    assert m.get_tile(Layer.Ground, -1, 0) is None
    assert m.get_tile(Layer.Ground, 2, 0) is None
    assert m.get_tile(Layer.Ground, 0, 5) is None

    # set_tile outside bounds should be a no-op (no exception)
    m.set_tile(Layer.Ground, 99, 99, "Z")


def test_post_pickle_reconstructs_fields(monkeypatch):
    # Simulate __setstate__ path by calling it directly with minimal state
    def fake_loader(matrix, codes=None):
        h = len(matrix)
        w = len(matrix[0]) if h else 0
        return [[types.SimpleNamespace(x=x, y=y, code=(codes[y][x] if codes else "")) for x in range(w)] for y in range(h)]

    monkeypatch.setattr(mm, 'load_tiles_from_text', fake_loader, raising=True)

    m = mm.Map(matrix=[".."], layers={Layer.Ground: [["a", "b"]]}, tiles_by_layer={Layer.Ground: fake_loader([".."], [["a","b"]])}, metadata={}, name="t")
    state = m.__getstate__()

    # Clear critical fields then restore via __setstate__
    m.tiles_by_layer = {}
    m.tiles = []
    m.overlay = None
    m.__setstate__(state)

    # After restore, tiles/overlay reconstructed
    assert m.overlay == m.layers.get(Layer.Ground)
    assert m.tiles and len(m.tiles[0]) == 2
