import types

import roguelike_engine.map.model.map_model as mm
from roguelike_engine.map.model.layer import Layer


def test_map_init_and_accessors_with_fake_loader(monkeypatch):
    # Fake loader returns a grid of simple objects with (x,y,code)
    def fake_loader(matrix, codes=None):
        h = len(matrix)
        w = len(matrix[0]) if h else 0
        return [[types.SimpleNamespace(x=x, y=y, code=(codes[y][x] if codes else "")) for x in range(w)] for y in range(h)]

    monkeypatch.setattr(mm, 'load_tiles_from_text', fake_loader, raising=True)

    matrix = ["..", "##"]
    ground_codes = [["g0", "g1"], ["g2", "g3"]]
    tiles_by_layer = {Layer.Ground: fake_loader(matrix, ground_codes)}
    m = mm.Map(matrix=matrix, layers={Layer.Ground: ground_codes}, tiles_by_layer=tiles_by_layer, metadata={}, name="test")

    # overlay mirrors Ground, tiles rebuilt by __post_init__ using fake loader
    assert m.overlay == ground_codes
    assert len(m.tiles) == 2 and len(m.tiles[0]) == 2

    # Accessors
    assert m.get_layer(Layer.Ground) == ground_codes
    assert m.get_tiles_for_layer(Layer.Ground)  # returns grid
    t00 = m.get_tile(Layer.Ground, 0, 0)
    assert getattr(t00, 'x', None) == 0 and getattr(t00, 'y', None) == 0


def test_set_tile_updates_layer_and_legacy_fields(monkeypatch):
    def fake_loader(matrix, codes=None):
        h = len(matrix)
        w = len(matrix[0]) if h else 0
        return [[types.SimpleNamespace(x=x, y=y, code=(codes[y][x] if codes else "")) for x in range(w)] for y in range(h)]

    monkeypatch.setattr(mm, 'load_tiles_from_text', fake_loader, raising=True)

    matrix = ["..", ".."]
    codes = [["a", "b"], ["c", "d"]]
    tiles_by_layer = {Layer.Ground: fake_loader(matrix, codes)}
    m = mm.Map(matrix=matrix, layers={Layer.Ground: [row[:] for row in codes]}, tiles_by_layer=tiles_by_layer, metadata={}, name="test")

    # Change one code in Ground via set_tile -> overlay and tiles must update
    m.set_tile(Layer.Ground, 1, 1, "Z")
    assert m.overlay[1][1] == "Z"
    assert m.tiles[1][1].code == "Z"

    # Change tile in a non-Ground layer -> tiles_by_layer updated
    other = Layer.Decorations
    m.layers[other] = [["", ""], ["", ""]]
    m.tiles_by_layer[other] = fake_loader(matrix, m.layers[other])
    m.set_tile(other, 0, 0, "D")
    assert m.tiles_by_layer[other][0][0].code == "D"
